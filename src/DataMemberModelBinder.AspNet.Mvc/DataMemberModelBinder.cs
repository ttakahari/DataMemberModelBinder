using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Web.Mvc;

namespace DataMemberModelBinder.AspNet.Mvc
{
    /// <summary>
    /// The class that maps a request to an object with a name of <see cref="DataMember" />.
    /// </summary>
    public class DataMemberModelBinder : DefaultModelBinder
    {
        /// <inheritdoc cref="DefaultModelBinder.OnModelUpdated(ControllerContext, ModelBindingContext)" />
        protected override void OnModelUpdated(ControllerContext controllerContext, ModelBindingContext bindingContext)
        {
            var startedValid = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var validationResult in new CompositeModelValidator(bindingContext.ModelMetadata, controllerContext).Validate(null))
            {
                string subPropertyName = CreateSubPropertyName(bindingContext.ModelName, validationResult.MemberName);

                if (!startedValid.ContainsKey(subPropertyName))
                {
                    startedValid[subPropertyName] = bindingContext.ModelState.IsValidField(subPropertyName);
                }

                if (startedValid[subPropertyName])
                {
                    bindingContext.ModelState.AddModelError(subPropertyName, validationResult.Message);
                }
            }
        }

        /// <inheritdoc cref="DefaultModelBinder.BindProperty(ControllerContext, ModelBindingContext, PropertyDescriptor)" />
        protected override void BindProperty(ControllerContext controllerContext, ModelBindingContext bindingContext, PropertyDescriptor propertyDescriptor)
        {
            var dataMember = propertyDescriptor.Attributes.OfType<DataMemberAttribute>().FirstOrDefault();

            if (dataMember == null)
            {
                base.BindProperty(controllerContext, bindingContext, propertyDescriptor);

                return;
            }

            var original = CreateSubPropertyName(bindingContext.ModelName, propertyDescriptor.Name);
            var alias = CreateSubPropertyName(bindingContext.ModelName, dataMember.Name);

            if (!bindingContext.ValueProvider.ContainsPrefix(alias))
            {
                return;
            }

            var binder = Binders.GetBinder(propertyDescriptor.PropertyType);
            var metadata = bindingContext.PropertyMetadata[propertyDescriptor.Name];

            metadata.Model = propertyDescriptor.GetValue(bindingContext.Model);

            var innerBindingContext = new ModelBindingContext
            {
                ModelMetadata = metadata,
                ModelName = alias,
                ModelState = bindingContext.ModelState,
                ValueProvider = bindingContext.ValueProvider
            };

            var value = GetPropertyValue(controllerContext, innerBindingContext, propertyDescriptor, binder);

            propertyDescriptor.SetValue(bindingContext.ModelMetadata.Model, value);

            metadata.Model = value;
        }

        private class CompositeModelValidator : ModelValidator
        {
            public CompositeModelValidator(ModelMetadata metadata, ControllerContext controllerContext)
                : base(metadata, controllerContext)
            { }

            public override IEnumerable<ModelValidationResult> Validate(object container)
            {
                bool propertiesValid = true;

                foreach (var property in Metadata.Properties.ToArray())
                {
                    foreach (var validator in property.GetValidators(ControllerContext))
                    {
                        foreach (var validationResult in validator.Validate(Metadata.Model))
                        {
                            propertiesValid = false;

                            yield return CreateSubPropertyResult(property, validationResult);
                        }
                    }
                }

                if (propertiesValid)
                {
                    foreach (var validator in Metadata.GetValidators(ControllerContext))
                    {
                        foreach (var validationResult in validator.Validate(container))
                        {
                            yield return validationResult;
                        }
                    }
                }
            }

            private static ModelValidationResult CreateSubPropertyResult(ModelMetadata propertyMetadata, ModelValidationResult propertyResult)
            {
                return new ModelValidationResult
                {
                    MemberName = CreateSubPropertyName(propertyMetadata.PropertyName, propertyResult.MemberName),
                    Message = propertyResult.Message
                };
            }
        }
    }

    /// <summary>
    /// The class that provides <see cref="DataMemberModelBinder"/> without regard to a type of an object.
    /// </summary>
    public class DataMemberModelBinderProvider : IModelBinderProvider
    {
        /// <inheritdoc cref="IModelBinderProvider.GetBinder(Type)" />
        public IModelBinder GetBinder(Type modelType)
        {
            return new DataMemberModelBinder();
        }
    }
}
