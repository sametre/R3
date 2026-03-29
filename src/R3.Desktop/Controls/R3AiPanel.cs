using Krypton.Toolkit;
using R3.Desktop.Theme;

namespace R3.Desktop.Controls;

internal sealed class R3AiPanel : Panel
{
    private readonly RichTextBox _conversation = new();
    private readonly KryptonTextBox _question = new();
    public event EventHandler<string>? QuestionSubmitted;
    public event EventHandler<string>? PdfSelected;

    public R3AiPanel()
    {
        Size=new Size(390,430);Anchor=AnchorStyles.Right|AnchorStyles.Bottom;BackColor=Color.White;
        Padding=new Padding(1);BorderStyle=BorderStyle.FixedSingle;Visible=false;
        var header=new Panel{Dock=DockStyle.Top,Height=54,BackColor=Color.FromArgb(15,23,42),Padding=new Padding(14,8,8,8)};
        header.Controls.Add(new Label{Text="R3 AI",Dock=DockStyle.Top,Height=22,ForeColor=Color.White,Font=new Font("Segoe UI",12,FontStyle.Bold)});
        header.Controls.Add(new Label{Text="Fatura ve müşteri asistanı",Dock=DockStyle.Bottom,Height=17,ForeColor=Color.FromArgb(148,163,184),Font=new Font("Segoe UI",8)});
        var close=new Button{Text="×",Dock=DockStyle.Right,Width=34,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(15,23,42),ForeColor=Color.White};close.FlatAppearance.BorderSize=0;close.Click+=(_,_)=>Visible=false;header.Controls.Add(close);
        _conversation.Dock=DockStyle.Fill;_conversation.ReadOnly=true;_conversation.BorderStyle=BorderStyle.None;_conversation.BackColor=Color.FromArgb(248,250,252);_conversation.Font=new Font("Segoe UI",9);_conversation.Text="R3 AI\nBir müşteri, fatura numarası veya belge hakkında soru sorabilirsiniz.\n\n";
        var input=new Panel{Dock=DockStyle.Bottom,Height=92,BackColor=Color.White,Padding=new Padding(10)};
        _question.Dock=DockStyle.Top;_question.Height=34;_question.CueHint.CueHintText="Örn: ABC müşterisinin son faturası";_question.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){Submit();e.SuppressKeyPress=true;}};
        var send=new Button{Text="Sor",Dock=DockStyle.Right,Width=68,FlatStyle=FlatStyle.Flat,BackColor=R3Colors.Primary,ForeColor=Color.White};send.Click+=(_,_)=>Submit();
        var pdf=new Button{Text="PDF Fatura",Dock=DockStyle.Left,Width=94,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=R3Colors.Text};pdf.Click+=(_,_)=>SelectPdf();
        var buttons=new Panel{Dock=DockStyle.Bottom,Height=32};buttons.Controls.Add(send);buttons.Controls.Add(pdf);input.Controls.Add(buttons);input.Controls.Add(_question);
        Controls.Add(_conversation);Controls.Add(input);Controls.Add(header);
    }

    public void Toggle(){Visible=!Visible;if(Visible){BringToFront();_question.Focus();}}
    public void AddAnswer(string text){_conversation.AppendText($"R3 AI\n{text}\n\n");_conversation.SelectionStart=_conversation.TextLength;_conversation.ScrollToCaret();}
    private void Submit(){string value=_question.Text.Trim();if(value.Length==0)return;_conversation.AppendText($"Siz\n{value}\n\n");_question.Clear();QuestionSubmitted?.Invoke(this,value);}
    private void SelectPdf(){using var dialog=new OpenFileDialog{Filter="PDF faturaları (*.pdf)|*.pdf",Title="R3 AI ile işlenecek faturayı seçin"};if(dialog.ShowDialog()==DialogResult.OK){_conversation.AppendText($"Siz\nPDF: {Path.GetFileName(dialog.FileName)}\n\n");PdfSelected?.Invoke(this,dialog.FileName);}}
}
