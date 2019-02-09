using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace DataMemberModelBinder.AspNetCore.Mvc
{
    /// <summary>
    /// The class that maps a request to an object with a name of <see cref="DataMemberAttribute" />.
    /// </summary>
    public class DataMemberModelBinder : IModelBinder
    {
        private readonly IReadOnlyDictionary<ModelMetadata, IModelBinder> _binders;

        /// <summary>
        /// Create a new instance of <see cref="DataMemberModelBinder"/> with receiveing the mappings of <see cref="IModelBinder"/>.
        /// </summary>
        /// <param name="binders">The mappings of <see cref="IModelBinder"/>.</param>
        public DataMemberModelBinder(IReadOnlyDictionary<ModelMetadata, IModelBinder> binders)
        {
            _binders = binders;
        }

        /// <inheritdoc cref="IModelBinder.BindModelAsync(ModelBindingContext)" />
        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (!CanCreateModel(bindingContext))
            {
                return;
            }

            if (bindingContext.Model == null)
            {
                bindingContext.Model = CreateModel(bindingContext);
            }

            var modelMetadata = bindingContext.ModelMetadata;
            var attemptedPropertyBinding = false;

            for (var i = 0; i < modelMetadata.Properties.Count; i++)
            {
                var property = modelMetadata.Properties[i];
                var dataMemberAttribute = ((DefaultModelMetadata)property).Attributes.Attributes.OfType<DataMemberAttribute>().FirstOrDefault();

                if (!CanBindProperty(bindingContext, property))
                {
                    continue;
                }

                object propertyModel = null;

                if (property.PropertyGetter != null && property.IsComplexType && !property.ModelType.IsArray)
                {
                    propertyModel = property.PropertyGetter(bindingContext.Model);
                }

                var fieldName = property.BinderModelName ?? property.PropertyName;
                var modelName = ModelNames.CreatePropertyModelName(bindingContext.ModelName, fieldName);

                ModelBindingResult result;

                using (bindingContext.EnterNestedScope(property, fieldName, modelName, propertyModel))
                {
                    var innerBindinContext = new DefaultModelBindingContext
                    {
                        ModelMetadata = property,
                        ModelName = dataMemberAttribute.Name,
                        ModelState = bindingContext.ModelState,
                        ValueProvider = bindingContext.ValueProvider
                    };

                    await BindProperty(innerBindinContext);

                    result = innerBindinContext.Result;
                }

                if (result.IsModelSet)
                {
                    attemptedPropertyBinding = true;

                    SetProperty(bindingContext, modelName, property, result);
                }
                else if (property.IsBindingRequired)
                {
                    attemptedPropertyBinding = true;

                    var message = property.ModelBindingMessageProvider.MissingBindRequiredValueAccessor(fieldName);

                    bindingContext.ModelState.TryAddModelError(modelName, message);
                }
            }

            if (!attemptedPropertyBinding && bindingContext.IsTopLevelObject && modelMetadata.IsBindingRequired)
            {
                var messageProvider = modelMetadata.ModelBindingMessageProvider;
                var message = messageProvider.MissingBindRequiredValueAccessor(bindingContext.FieldName);

                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, message);
            }

            bindingContext.Result = ModelBindingResult.Success(bindingContext.Model);
        }

        private bool CanCreateModel(ModelBindingContext bindingContext)
        {
            var isTopLevelObject = bindingContext.IsTopLevelObject;
            var bindingSource = bindingContext.BindingSource;

            if (!isTopLevelObject && bindingSource != null && bindingSource.IsGreedy) return false;
            if (isTopLevelObject) return true;
            if (CanBindAnyModelProperties(bindingContext)) return true;

            return false;
        }

        private bool CanBindAnyModelProperties(ModelBindingContext bindingContext)
        {
            if (bindingContext.ModelMetadata.Properties.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < bindingContext.ModelMetadata.Properties.Count; i++)
            {
                var propertyMetadata = bindingContext.ModelMetadata.Properties[i];

                if (!CanBindProperty(bindingContext, propertyMetadata))
                {
                    continue;
                }

                var bindingSource = propertyMetadata.BindingSource;

                if (bindingSource != null && bindingSource.IsGreedy)
                {
                    return true;
                }

                var fieldName = propertyMetadata.BinderModelName ?? propertyMetadata.PropertyName;
                var modelName = ModelNames.CreatePropertyModelName(bindingContext.ModelName, fieldName);

                using (bindingContext.EnterNestedScope(propertyMetadata, fieldName, modelName, null))
                {
                    if (bindingContext.ValueProvider.ContainsPrefix(bindingContext.ModelName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool CanBindProperty(ModelBindingContext bindingContext, ModelMetadata propertyMetadata)
        {
            var metadataProviderFilter = bindingContext.ModelMetadata.PropertyFilterProvider?.PropertyFilter;

            if (metadataProviderFilter?.Invoke(propertyMetadata) == false) return false;
            if (bindingContext.PropertyFilter?.Invoke(propertyMetadata) == false) return false;
            if (!propertyMetadata.IsBindingAllowed) return false;
            if (!CanUpdatePropertyInternal(propertyMetadata)) return false;

            return true;
        }

        private static bool CanUpdatePropertyInternal(ModelMetadata propertyMetadata)
            => !propertyMetadata.IsReadOnly || CanUpdateReadOnlyProperty(propertyMetadata.ModelType);

        private static bool CanUpdateReadOnlyProperty(Type propertyType)
        {
            if (propertyType.GetTypeInfo().IsValueType) return false;
            if (propertyType.IsArray) return false;
            if (propertyType == typeof(string)) return false;

            return true;
        }

        protected virtual object CreateModel(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            var modelTypeInfo = bindingContext.ModelType.GetTypeInfo();

            if (modelTypeInfo.IsAbstract || modelTypeInfo.GetConstructor(Type.EmptyTypes) == null)
            {
                var metadata = bindingContext.ModelMetadata;

                switch (metadata.MetadataKind)
                {
                    case ModelMetadataKind.Parameter:
                    case ModelMetadataKind.Property:
                    case ModelMetadataKind.Type:
                        throw new InvalidOperationException();
                }
            }

            var modelCreator = Expression
                .Lambda<Func<object>>(Expression.New(bindingContext.ModelType))
                .Compile();

            return modelCreator.Invoke();
        }

        private Task BindProperty(ModelBindingContext bindingContext)
        {
            var binder = _binders[bindingContext.ModelMetadata];

            return binder.BindModelAsync(bindingContext);
        }

        private void SetProperty(ModelBindingContext bindingContext, string modelName, ModelMetadata propertyMetadata, ModelBindingResult result)
        {
            if (bindingContext == null) throw new ArgumentNullException(nameof(bindingContext));
            if (modelName == null) throw new ArgumentNullException(nameof(modelName));
            if (propertyMetadata == null) throw new ArgumentNullException(nameof(propertyMetadata));

            if (!result.IsModelSet) return;
            if (propertyMetadata.IsReadOnly) return;

            var value = result.Model;

            try
            {
                propertyMetadata.PropertySetter(bindingContext.Model, value);
            }
            catch (Exception exception)
            {
                AddModelError(exception, modelName, bindingContext);
            }
        }

        private static void AddModelError(Exception exception, string modelName, ModelBindingContext bindingContext)
        {
            var targetInvocationException = exception as TargetInvocationException;

            if (targetInvocationException?.InnerException != null)
            {
                exception = targetInvocationException.InnerException;
            }

            var modelState = bindingContext.ModelState;
            var validationState = modelState.GetFieldValidationState(modelName);

            if (validationState == ModelValidationState.Unvalidated)
            {
                modelState.AddModelError(modelName, exception, bindingContext.ModelMetadata);
            }
        }
    }

    /// <summary>
    /// The class that provides <see cref="DataMemberModelBinder"/> without regard to a type of an object.
    /// </summary>
    public class DataMemberModelBinderProvider : IModelBinderProvider
    {
        /// <inheritdoc cref="IModelBinderProvider.GetBinder(ModelBinderProviderContext)" />
        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.IsComplexType && !context.Metadata.IsCollectionType)
            {
                var binders = new Dictionary<ModelMetadata, IModelBinder>();

                for (var i = 0; i < context.Metadata.Properties.Count; i++)
                {
                    var property = context.Metadata.Properties[i];

                    binders.Add(property, context.CreateBinder(property));
                }

                return new DataMemberModelBinder(binders);
            }

            return null;
        }
    }
}
