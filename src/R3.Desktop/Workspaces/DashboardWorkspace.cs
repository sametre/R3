using R3.Application.Dashboard;
using R3.Desktop.Controls;
using R3.Desktop.Theme;

namespace R3.Desktop.Workspaces;

internal sealed class DashboardWorkspace : Panel
{
    private readonly FlowLayoutPanel _cards=new();
    private readonly FlowLayoutPanel _agendaList=new();
    private readonly MonthCalendar _calendar=new();
    private readonly Label _periodTitle=new();
    private DashboardSnapshot? _snapshot;
    private string _view="Ay";
    public event EventHandler<string>? ModuleRequested;
    public event EventHandler<(DateTime From,DateTime To)>? PeriodRequested;
    public event EventHandler<DateTime>? NewNoteRequested;

    public DashboardWorkspace()
    {
        Dock=DockStyle.Fill;BackColor=R3Colors.Canvas;Padding=new Padding(20);
        var header=new Panel{Dock=DockStyle.Top,Height=68};
        header.Controls.Add(new Label{Text="Gösterge Paneli",Dock=DockStyle.Top,Height=35,Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=R3Colors.Text});
        header.Controls.Add(new Label{Text="Finansal görünüm, yaklaşan vadeler ve işletme ajandası",Dock=DockStyle.Bottom,Height=25,Font=new Font("Segoe UI",9),ForeColor=R3Colors.MutedText});

        _cards.Dock=DockStyle.Top;_cards.Height=112;_cards.WrapContents=false;_cards.Padding=new Padding(0,8,0,8);
        foreach(var card in new[]{("Alacak Vadesi","0,00 ₺",Color.FromArgb(14,165,233)),("Borç Vadesi","0,00 ₺",Color.FromArgb(249,115,22)),("Açık Çek","0",Color.FromArgb(139,92,246)),("Açık Senet","0",Color.FromArgb(236,72,153)),("Taslak Fatura","0",Color.FromArgb(16,185,129))})_cards.Controls.Add(CreateMetric(card.Item1,card.Item2,card.Item3));

        var split=new SplitContainer{Dock=DockStyle.Fill,SplitterDistance=360,BackColor=R3Colors.Canvas,Panel1MinSize=300};
        split.Panel1.Padding=new Padding(0,12,8,0);split.Panel2.Padding=new Padding(8,12,0,0);
        split.Panel1.Controls.Add(CreateCalendarPanel());split.Panel2.Controls.Add(CreateAgendaPanel());
        Controls.Add(split);Controls.Add(_cards);Controls.Add(header);
        _calendar.DateSelected+=(_,_)=>RequestPeriod();
        DateTime month=new(DateTime.Today.Year,DateTime.Today.Month,1);_periodTitle.Text=$"{month:dd MMMM yyyy}  —  {month.AddMonths(1).AddDays(-1):dd MMMM yyyy}";
    }

    public void SetSnapshot(DashboardSnapshot snapshot)
    {
        _snapshot=snapshot;
        SetMetric(0,$"{snapshot.ReceivablesDue:N2} ₺");SetMetric(1,$"{snapshot.PayablesDue:N2} ₺");SetMetric(2,snapshot.OpenChequeCount.ToString("N0",System.Globalization.CultureInfo.CurrentCulture));SetMetric(3,snapshot.OpenNoteCount.ToString("N0",System.Globalization.CultureInfo.CurrentCulture));SetMetric(4,snapshot.DraftInvoiceCount.ToString("N0",System.Globalization.CultureInfo.CurrentCulture));
        RenderAgenda();
    }

    private Control CreateCalendarPanel()
    {
        var panel=Surface();panel.Padding=new Padding(18);
        var quick=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=96,FlowDirection=FlowDirection.LeftToRight};
        quick.Controls.Add(Quick("Finans","finance",Color.FromArgb(234,179,8)));quick.Controls.Add(Quick("Çekler","cheques",Color.FromArgb(139,92,246)));quick.Controls.Add(Quick("Senetler","promissory-notes",Color.FromArgb(236,72,153)));
        _calendar.Dock=DockStyle.Fill;_calendar.MaxSelectionCount=1;_calendar.Font=new Font("Segoe UI",10);_calendar.TitleBackColor=Color.FromArgb(30,64,175);_calendar.TitleForeColor=Color.White;_calendar.TrailingForeColor=Color.FromArgb(148,163,184);
        panel.Controls.Add(_calendar);panel.Controls.Add(quick);panel.Controls.Add(new Label{Text="AJANDA TAKVİMİ",Dock=DockStyle.Top,Height=34,Font=new Font("Segoe UI Semibold",10),ForeColor=R3Colors.Text});
        return panel;
    }

    private Control CreateAgendaPanel()
    {
        var panel=Surface();panel.Padding=new Padding(16);
        var toolbar=new Panel{Dock=DockStyle.Top,Height=42};var add=new Button{Text="+ Not Ekle",Dock=DockStyle.Right,Width=86,Height=29,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(16,185,129),ForeColor=Color.White};add.Click+=(_,_)=>NewNoteRequested?.Invoke(this,_calendar.SelectionStart);toolbar.Controls.Add(add);_periodTitle.Dock=DockStyle.Fill;_periodTitle.Font=new Font("Segoe UI",11,FontStyle.Bold);_periodTitle.TextAlign=ContentAlignment.MiddleLeft;toolbar.Controls.Add(_periodTitle);
        var views=new FlowLayoutPanel{Dock=DockStyle.Right,Width=230,FlowDirection=FlowDirection.RightToLeft};
        foreach(string view in new[]{"Ay","Hafta","Gün"}){var b=new Button{Text=view,Width=66,Height=29,FlatStyle=FlatStyle.Flat,BackColor=view=="Ay"?R3Colors.Primary:Color.White,ForeColor=view=="Ay"?Color.White:R3Colors.Text,Tag=view,Margin=new Padding(4,4,0,0)};b.Click+=(_,_)=>{_view=(string)b.Tag!;foreach(Button x in views.Controls){x.BackColor=x==b?R3Colors.Primary:Color.White;x.ForeColor=x==b?Color.White:R3Colors.Text;}RequestPeriod();};views.Controls.Add(b);}toolbar.Controls.Add(views);
        _agendaList.Dock=DockStyle.Fill;_agendaList.AutoScroll=true;_agendaList.FlowDirection=FlowDirection.TopDown;_agendaList.WrapContents=false;_agendaList.Padding=new Padding(0,8,4,4);
        panel.Controls.Add(_agendaList);panel.Controls.Add(toolbar);return panel;
    }

    private void RequestPeriod()
    {
        DateTime selected=_calendar.SelectionStart.Date;DateTime from,to;
        if(_view=="Gün"){from=selected;to=selected.AddDays(1);}
        else if(_view=="Hafta"){int offset=((int)selected.DayOfWeek+6)%7;from=selected.AddDays(-offset);to=from.AddDays(7);}
        else{from=new DateTime(selected.Year,selected.Month,1);to=from.AddMonths(1);}
        _periodTitle.Text=$"{from:dd MMMM yyyy}  —  {to.AddDays(-1):dd MMMM yyyy}";
        PeriodRequested?.Invoke(this,(from,to));
    }

    private void RenderAgenda()
    {
        _agendaList.Controls.Clear();var items=_snapshot?.Agenda??[];
        if(items.Count==0){_agendaList.Controls.Add(new Label{Text="Bu dönemde ödeme, çek, senet veya ajanda notu bulunmuyor.",Width=600,Height=100,TextAlign=ContentAlignment.MiddleCenter,ForeColor=R3Colors.MutedText});return;}
        foreach(AgendaItem item in items)_agendaList.Controls.Add(AgendaRow(item));
    }

    private static Control AgendaRow(AgendaItem item)
    {
        Color color=item.Type switch{"Ödeme"=>Color.FromArgb(249,115,22),"Çek"=>Color.FromArgb(139,92,246),"Senet"=>Color.FromArgb(236,72,153),_=>Color.FromArgb(14,165,233)};
        var row=new Panel{Width=720,Height=66,BackColor=Color.White,Margin=new Padding(0,0,0,7),Padding=new Padding(12,8,12,8)};
        row.Controls.Add(new Panel{Dock=DockStyle.Left,Width=4,BackColor=color});
        row.Controls.Add(new Label{Text=item.Amount.HasValue?$"{item.Amount:N2} {item.CurrencyCode}":"",Dock=DockStyle.Right,Width=135,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI",9,FontStyle.Bold),ForeColor=color});
        var text=new Panel{Dock=DockStyle.Fill,Padding=new Padding(12,0,0,0)};text.Controls.Add(new Label{Text=$"{item.Date:dd MMM, ddd}  •  {item.Type}",Dock=DockStyle.Top,Height=21,ForeColor=color,Font=new Font("Segoe UI Semibold",8.5F)});text.Controls.Add(new Label{Text=item.Title,Dock=DockStyle.Fill,ForeColor=R3Colors.Text,Font=new Font("Segoe UI",9.5F)});row.Controls.Add(text);return row;
    }

    private static Control CreateMetric(string title,string value,Color color){var p=new Panel{Width=205,Height=88,BackColor=Color.White,Margin=new Padding(0,0,12,0),Padding=new Padding(16,12,12,10),Tag="metric"};p.Controls.Add(new Panel{Dock=DockStyle.Left,Width=4,BackColor=color});p.Controls.Add(new Label{Name="Value",Text=value,Dock=DockStyle.Bottom,Height=38,Font=new Font("Segoe UI",16,FontStyle.Bold),ForeColor=R3Colors.Text,TextAlign=ContentAlignment.MiddleLeft});p.Controls.Add(new Label{Text=title.ToUpperInvariant(),Dock=DockStyle.Top,Height=22,Font=new Font("Segoe UI Semibold",8),ForeColor=R3Colors.MutedText});return p;}
    private void SetMetric(int index,string value){if(_cards.Controls[index].Controls["Value"] is Label label)label.Text=value;}
    private Button Quick(string text,string key,Color color){var b=new Button{Text=text,Tag=key,Width=94,Height=62,Margin=new Padding(0,8,8,0),FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=color,Font=new Font("Segoe UI",8.5F,FontStyle.Bold)};b.FlatAppearance.BorderColor=color;b.Click+=(_,_)=>ModuleRequested?.Invoke(this,key);return b;}
    private static Panel Surface()=>new(){Dock=DockStyle.Fill,BackColor=Color.White};
}
