using System.Runtime.Serialization;

namespace AspNetCoreMvcSample.Models
{
    [DataContract]
    public class DemoFormModel
    {
        [DataMember(Name = "foo")]
        public string TextBox { get; set; }

        [DataMember(Name = "bar")]
        public int DropdownList { get; set; }

        [DataMember(Name = "baz")]
        public int[] CheckBoxList { get; set; }
    }
}
