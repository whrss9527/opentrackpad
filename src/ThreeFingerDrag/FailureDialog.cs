using System.Drawing;
using System.Windows.Forms;
using OpenTrackpadUi;

namespace ThreeFingerDrag {
  // Exceptions belong in the local diagnostic log. This surface accepts only
  // lifecycle state, so paths, framework names and stack traces cannot leak in.
  sealed class FailureDialog : Form {
    internal static string MessageFor(bool canContinue) {
      return canContinue?"输入功能已暂停。你可以返回设置重新启用；如果再次出现，可复制诊断信息后重新打开应用。":"应用暂时无法继续运行。请关闭后重新打开；已有的设置与原始备份会保留在本机。";
    }
    internal FailureDialog(bool canContinue) {
      Text="Open Trackpad";ShowIcon=false;ShowInTaskbar=false;
      StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;
      MaximizeBox=false;MinimizeBox=false;AutoScaleMode=AutoScaleMode.Dpi;
      AutoScaleDimensions=new SizeF(96,96);ClientSize=new Size(516,288);
      BackColor=Palette.Canvas;Font=Palette.Font(9.5f,FontStyle.Regular);
      var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(28,24,28,24),ColumnCount=1,RowCount=4,BackColor=Palette.Canvas};
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
      Controls.Add(layout);
      Label brand=Palette.Label("OPEN TRACKPAD",9,Palette.Accent);brand.Dock=DockStyle.Fill;layout.Controls.Add(brand,0,0);
      Label heading=Palette.Label(canContinue?"刚才遇到了一点问题":"暂时无法打开应用",19,Palette.Ink);heading.Dock=DockStyle.Fill;layout.Controls.Add(heading,0,1);
      Label detail=Palette.Label(MessageFor(canContinue),10,Palette.Muted);detail.Dock=DockStyle.Fill;detail.TextAlign=ContentAlignment.TopLeft;detail.Padding=new Padding(0,4,0,12);layout.Controls.Add(detail,0,2);
      var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty};layout.Controls.Add(actions,0,3);
      var primary=new ActionButton{Text=canContinue?"返回设置":"关闭应用",Primary=true,Width=124,Height=40,DialogResult=DialogResult.OK,Margin=new Padding(8,0,0,0)};actions.Controls.Add(primary);AcceptButton=primary;CancelButton=primary;
      if(canContinue)actions.Controls.Add(new ActionButton{Text="退出应用",Width=112,Height=40,DialogResult=DialogResult.Abort,Margin=Padding.Empty});
      Shown+=delegate{Palette.Apply(this);primary.Focus();};
    }
  }
}
