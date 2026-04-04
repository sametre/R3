using Krypton.Toolkit;
using R3.Desktop.Branding;
using R3.Desktop.Theme;

namespace R3.Desktop.Dialogs;

internal sealed class AgendaNoteDialog:KryptonForm
{
 private readonly KryptonTextBox _title=new(),_description=new();private readonly KryptonDateTimePicker _date=new(){Format=DateTimePickerFormat.Custom,CustomFormat="dd.MM.yyyy HH:mm"};
 public string NoteTitle=>_title.Text.Trim();public string? NoteDescription=>string.IsNullOrWhiteSpace(_description.Text)?null:_description.Text.Trim();public DateTime EventDate=>_date.Value;
 public AgendaNoteDialog(DateTime date)
 {
  Text="R3 • Ajanda Notu";Icon=R3Branding.AppIcon;StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(480,300);_date.Value=date.Date.AddHours(9);
  var t=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(22),BackColor=Color.White};
  Add(t,"Tarih ve Saat",_date);Add(t,"Başlık",_title);Add(t,"Açıklama",_description);
  var save=R3Theme.CreatePrimaryButton("Notu Kaydet");save.Dock=DockStyle.Bottom;save.Height=38;save.Click+=(_,_)=>{if(NoteTitle.Length==0)return;DialogResult=DialogResult.OK;Close();};t.Controls.Add(save);Controls.Add(t);AcceptButton=save;
 }
 private static void Add(TableLayoutPanel t,string label,Control control){t.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,Height=22,ForeColor=R3Colors.MutedText});control.Dock=DockStyle.Fill;control.Height=34;t.Controls.Add(control);}
}
