// For examples, see:
// https://thegraybook.vvvv.org/reference/extending/writing-nodes.html#examples

namespace HDE;

[ProcessNode]
public class Process
{
    private int _value;

    public int Update(int increment)
    {
        return _value += increment;
    }
}