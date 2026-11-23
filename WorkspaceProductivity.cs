using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Sputnik;

public partial class MainWindow
{
    private void ShowCommands()
    {
        var page = ToolPage("Быстрые команды");
        var input = Input("Команда", max:10000); input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.MinHeight = 64; input.MaxHeight = 130;
        Field(page,"Что сделать",input);
        page.Children.Add(Text("заметка Купить молоко\nнапомни завтра в 10:00 позвонить\nнапомни через 20 минут проверить духовку\nтаймер на 25 минут", "Muted",12));
        var preview = Text(""); preview.Margin = new Thickness(0,12,0,6); page.Children.Add(preview);
        QuickCommand? draft = null;
        var apply = Button("Подтвердить",() =>
        {
            if (draft is null) return;
            var command = draft;
            if (command.Kind == "reminder" && command.Due <= DateTimeOffset.Now.ToUnixTimeMilliseconds()) throw new InvalidOperationException("Время из предпросмотра уже прошло. Обновите команду.");
            if (command.Kind == "note") Desktop?.CreateNote(command.Text);
            else if (command.Kind == "focus") new FocusClock(data).Start(DateTimeOffset.Now,command.Minutes);
            else UpdateWorkspace(state => Items(state,"reminders").Add(new JsonObject { ["id"]=Guid.NewGuid().ToString("N"),["title"]=command.Text,["due"]=command.Due,["repeat"]="none",["done"]=false }));
            draft = null; input.Clear(); preview.Text = "Готово.";
        },true);
        apply.IsEnabled = false;
        var parse = Button("Предпросмотр",() =>
        {
            try { draft = QuickCommand.Parse(input.Text,DateTimeOffset.Now); preview.Text = draft.Preview; apply.IsEnabled = true; }
            catch(Exception error) { draft = null; apply.IsEnabled = false; preview.Text = error.Message; }
        });
        input.TextChanged += (_,_) => { draft = null; apply.IsEnabled = false; preview.Text = ""; };
        page.Children.Add(Actions(parse,apply));
    }
    private void ShowFocus()
    {
        var page = ToolPage("Фокус"); var clock = new FocusClock(data); var state = clock.Load();
        var label = Text("", "Accent",22); var remaining = Text("",size:36);
        page.Children.Add(label); page.Children.Add(remaining);
        var actions = new WrapPanel(); page.Children.Add(actions);
        Button? startButton=null;
        void Refresh()
        {
            var current = clock.Load(); string phase = current["phase"]!.ToString();
            label.Text = phase == "work" ? "Работа" : phase == "long" ? "Длинный перерыв" : "Перерыв";
            var span = TimeSpan.FromMilliseconds(clock.Remaining(DateTimeOffset.Now)); remaining.Text = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
            if(startButton is not null)startButton.Content=current["running"]!.GetValue<bool>()?"Пауза":"Начать";
        }
        startButton=Button(state["running"]!.GetValue<bool>() ? "Пауза" : "Начать",() =>
        {
            if (clock.Load()["running"]!.GetValue<bool>()) clock.Pause(DateTimeOffset.Now); else clock.Start(DateTimeOffset.Now); ShowFocus();
        },true);actions.Children.Add(startButton);
        actions.Children.Add(Button("Сбросить",() => { clock.Reset(); ShowFocus(); }));
        page.Children.Add(Text("Завершено рабочих интервалов: " + state["completed"],"Muted",12));
        var work = Input("Работа, минут",state["work"]!.ToString(),3);var rest = Input("Перерыв, минут",state["short"]!.ToString(),3);
        var longer = Input("Длинный перерыв, минут",state["long"]!.ToString(),3);var every = Input("После интервалов",state["every"]!.ToString(),3);
        Row(page,Labeled("Работа, мин",work),Labeled("Перерыв, мин",rest)); Row(page,Labeled("Длинный, мин",longer),Labeled("Через интервалов",every));
        var auto = new CheckBox { Content = "Автоматически начинать следующий этап", IsChecked = state["auto"]?.GetValue<bool>()==true };page.Children.Add(auto);
        var status = Status();page.Children.Add(status);
        page.Children.Add(Button("Сохранить настройки",() =>
        {
            try { clock.Configure(int.Parse(work.Text),int.Parse(rest.Text),int.Parse(longer.Text),int.Parse(every.Text),auto.IsChecked==true);ShowFocus(); }
            catch(Exception error) { status.Text=error is FormatException ? "Введите целые числа." : error.Message; }
        }));
        var tick = new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) }; tick.Tick += (_,_) => Refresh();
        page.Loaded += (_,_) => tick.Start(); page.Unloaded += (_,_) => tick.Stop(); Refresh();
    }
    private void ShowProfiles()
    {
        var manager = new PersonalProfiles(data); var value = manager.Load(); var page = ToolPage("Профили");
        page.Children.Add(Text("Профиль хранит свои заметки, задачи, избранное, сочетания клавиш, даты, напоминания и тихие часы. Питомец, чат и API остаются общими.","Muted",12));
        foreach(var item in value["items"]!.AsArray())
        {
            string id = item!["id"]!.ToString(), name = item["name"]!.ToString(); bool active = id==value["active"]!.ToString();
            var card=new StackPanel { Margin=new Thickness(0,6,0,8) };card.Children.Add(Text(name+(active?" · активен":"")));
            var rename = Input("Название профиля",name,60);
            card.Children.Add(rename);
            card.Children.Add(Actions(Button("Переименовать",() => { manager.Rename(id,rename.Text);ShowProfiles(); }),
                Button(active?"Активен":"Переключить",() => { if(Desktop is {} app) app.SwitchProfile(id);else manager.Switch(id);ShowProfiles(); }),
                Button("Удалить…",() =>
                {
                    if(active) throw new InvalidOperationException("Сначала переключитесь в другой профиль.");
                    card.Children.Add(Actions(Button("Удалить профиль навсегда",() => {manager.Delete(id);ShowProfiles();},true),Button("Отмена",ShowProfiles)));
                })));
            page.Children.Add(card);
        }
        var title=Input("Имя нового профиля","",60);Field(page,"Новый профиль",title);
        var copy=new CheckBox {Content="Скопировать текущие данные",IsChecked=false};page.Children.Add(copy);
        page.Children.Add(Button("Создать профиль",() => { Desktop?.FlushNotes();manager.Create(title.Text,copy.IsChecked==true);ShowProfiles(); },true));
    }
}
