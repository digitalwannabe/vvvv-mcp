// Steps to implement your own enum based on this template:
// 1) Rename "DynamicEnumTemplate" to what your enum should be named
// 2) Rename "DynamicEnumTemplateDefinition" accordingly
// 3) Implement the definitions GetEntries() 
// 
// For more details regarding the template, see:
// https://thegraybook.vvvv.org/reference/extending/writing-nodes.html#dynamic-enums

using System.Reactive.Linq;
using VL.Core.CompilerServices;

namespace HDE;

[Serializable]
public class DynamicEnumTemplate : DynamicEnumBase<DynamicEnumTemplate, DynamicEnumTemplateDefinition>
{
    public DynamicEnumTemplate(string value) : base(value)
    {
    }

    [CreateDefault]
    public static DynamicEnumTemplate CreateDefault()
    {
        return CreateDefaultBase();
    }
}

public class DynamicEnumTemplateDefinition : DynamicEnumDefinitionBase<DynamicEnumTemplateDefinition>
{
    //Return the current enum entries
    protected override IReadOnlyDictionary<string, object> GetEntries()
    {
        var entries = new Dictionary<string, object>();

        entries.Add("foo", null);
        entries.Add("bar", null);

        return entries;
    }

    //Optionally trigger a change of your enum. This will in turn call GetEntries() again
    protected override IObservable<object> GetEntriesChangedObservable()
    {
        //e.g.: return HardwareChangedEvents.HardwareChanged; //reports device (e.g. usb) addition/removal
        return Observable.Empty<object>();
    }

    //Optionally disable alphabetic sorting
    protected override bool AutoSortAlphabetically => false; //true is the default
}