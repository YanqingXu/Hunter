using YouYou.Framework;

public sealed class FrameworkTestForm : UIFormBase
{
    public int Opened;
    public object Payload;
    public override void OnOpen(object value) { Opened++; Payload = value; }
}
