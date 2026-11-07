using System;
using System.Windows.Forms;

namespace ThreeFingerDrag {
  sealed class DiagnosticLogBox:TextBox {
    internal event Action UserReading;
    internal bool IgnoreReadingGestures;
    void Reading(){if(!IgnoreReadingGestures && UserReading!=null)UserReading();}
    protected override void OnMouseWheel(MouseEventArgs args){base.OnMouseWheel(args);if(args.Delta>0)Reading();}
    protected override void OnMouseUp(MouseEventArgs args){base.OnMouseUp(args);if(SelectionLength>0)Reading();}
    protected override void OnKeyUp(KeyEventArgs args){base.OnKeyUp(args);if(SelectionLength>0 || args.KeyCode==Keys.PageUp || args.KeyCode==Keys.Up || args.KeyCode==Keys.Home)Reading();}
    protected override void WndProc(ref Message message){base.WndProc(ref message);if(message.Msg==0x115)Reading();}
  }
}
