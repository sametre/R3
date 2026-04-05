using Krypton.Toolkit;
using R3.Desktop.Branding;
using R3.Desktop.Theme;

namespace R3.Desktop.Dialogs;

internal sealed class R3ConfirmationDialog:KryptonForm
{
 public R3ConfirmationDialog(string title,string message,string confirmText)
 {
  Text=$"R3 • {title}";Icon=R3Branding.AppIcon;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ClientSize=new Size(470,220);
  var body=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(26,22,26,14)};
  body.Controls.Add(new Label{Text=message,Dock=DockStyle.Fill,ForeColor=R3Colors.Text,Font=new Font("Segoe UI",10),TextAlign=ContentAlignment.MiddleLeft});
  body.Controls.Add(new Label{Text=title,Dock=DockStyle.Top,Height=38,ForeColor=R3Colors.Text,Font=new Font("Segoe UI Semibold",15,FontStyle.Bold)});
  var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=60,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(12),BackColor=R3Colors.Canvas};
  var confirm=R3Theme.CreatePrimaryButton(confirmText);confirm.Width=120;confirm.Click+=(_,_)=>DialogResult=DialogResult.OK;
  var cancel=new KryptonButton{Text="Vazgeç",Width=90,Height=36,Margin=new Padding(6)};cancel.Click+=(_,_)=>DialogResult=DialogResult.Cancel;
  footer.Controls.Add(confirm);footer.Controls.Add(cancel);Controls.Add(body);Controls.Add(footer);AcceptButton=confirm;CancelButton=cancel;
 }
 public static bool Confirm(IWin32Window owner,string title,string message,string confirmText){using var dialog=new R3ConfirmationDialog(title,message,confirmText);return dialog.ShowDialog(owner)==DialogResult.OK;}
}
