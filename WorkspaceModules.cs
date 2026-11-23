using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Sputnik;

public partial class MainWindow
{
    private void ShowSync()
    {
        var page=ToolPage("Синхронизация");var sync=new FolderSync(data);var config=sync.Configuration;
        page.Children.Add(Text("Общая папка для активного профиля",size:15));
        page.Children.Add(Text("Выберите одну папку на обоих компьютерах: сетевую или папку облачного клиента. Передаются заметки, избранное, даты, напоминания и задачи. Питомцы, чат, API-ключи и горячие клавиши остаются на этом компьютере.","Muted",12));
        var enabled=new CheckBox{Content="Включить синхронизацию",IsChecked=config["enabled"]?.GetValue<bool>()==true,Margin=new Thickness(0,10,0,6)};page.Children.Add(enabled);
        var folder=Input("Общая папка",config["folder"]?.ToString()??"",2000);Field(page,"Папка",folder);
        page.Children.Add(Button("Выбрать папку…",()=>
        {
#if NETFRAMEWORK
            var picker=new OpenFileDialog{Title="Выберите файл в общей папке",CheckFileExists=false,FileName="Общая папка"};
            if(picker.ShowDialog(this)==true)folder.Text=System.IO.Path.GetDirectoryName(picker.FileName)!;
#else
            var picker=new OpenFolderDialog{Title="Общая папка Спутника",Multiselect=false};if(picker.ShowDialog(this)==true)folder.Text=picker.FolderName;
#endif
        }));
        var status=Status();status.Text=FeatureFiles.Object(data,"sync-status.json")[sync.Active]?.ToString()??"Синхронизация ещё не выполнялась.";page.Children.Add(status);
        page.Children.Add(Actions(Button("Сохранить",()=>{sync.Configure(folder.Text,enabled.IsChecked==true);status.Text="Настройки сохранены.";},true),AsyncButton("Синхронизировать сейчас",async()=>
        {
            try{status.Text="Синхронизация…";status.Text=Desktop is {} app?await app.SyncNow():"Для синхронизации запустите установленное приложение.";}
            catch(Exception error){status.Text=error.Message;}
        })));
        page.Children.Add(Text("Изменения проверяются раз в минуту. При одновременном редактировании сохраняется копия с пометкой «конфликт». Удаления запоминаются для офлайн-компьютеров. Общая папка содержит читаемые файлы JSON: предоставляйте доступ только нужным людям.","Muted",12));
        page.Children.Add(Button("Открыть резервные копии синхронизации",()=>
        {
            string path=System.IO.Path.Combine(data,"sync-backups");System.IO.Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo("explorer.exe","\""+path+"\""){UseShellExecute=true});
        }));
    }
    private void ShowModules()
    {
        var page=ToolPage("Модули");var settings=FeatureFiles.Object(data,"modules.json");
        page.Children.Add(Text("Включите нужные модули. Погода обращается к Open-Meteo только после включения и выбора города.","Muted",12));
        void Toggle(string key,string title)
        {
            var check=new CheckBox{Content=title,IsChecked=settings[key]?.GetValue<bool>()==true,Margin=new Thickness(0,6,0,6)};
            check.Click+=(_,_)=>{try{var current=FeatureFiles.Object(data,"modules.json");current[key]=check.IsChecked==true;FeatureFiles.Write(data,"modules.json",current);ShowModules();}catch(Exception error){ShowError(error);}};page.Children.Add(check);
        }
        Toggle("weather","Погода");
        if(settings["weather"]?.GetValue<bool>()==true)BuildWeather(page,settings);
        Toggle("calendar","Ближайшие события");
        if(settings["calendar"]?.GetValue<bool>()==true)
        {
            var next=(WorkspaceData()["reminders"] as JsonArray??new()).OfType<JsonObject>().Where(i=>!ReminderScheduler.IsDone(i)).OrderBy(ReminderScheduler.Due).Take(5).ToArray();
            foreach(var item in next)Entry(page,Value(item,"title"),ReminderScheduler.AsLocalTime(ReminderScheduler.Due(item))?.ToString("g")??"",Button("Открыть",()=>EditReminder(item)));
            foreach(var item in new PersonalDates(data).Load().OrderBy(d=>d.Next(DateTime.Today)).Take(3))Entry(page,item.Name,item.Next(DateTime.Today).ToString("dd.MM"),Button("Открыть",()=>EditPersonalDate(item)));
            page.Children.Add(Button("Весь календарь",()=>NavigateTool("calendar")));
        }
        Toggle("tasks","Задачи");
        if(settings["tasks"]?.GetValue<bool>()==true)
        {
            var manager=new PersonalTasks(data);var input=Input("Новая задача",max:200);Field(page,"Новая задача",input);
            page.Children.Add(Button("Добавить задачу",()=>{manager.Add(input.Text);ShowModules();},true));
            foreach(var item in manager.Load().OrderBy(i=>i!["done"]!.GetValue<bool>()))
            {
                string id=item!["id"]!.ToString();Entry(page,item["title"]!.ToString(),item["done"]!.GetValue<bool>()?"Выполнено":"В работе",
                    Button(item["done"]!.GetValue<bool>()?"Вернуть":"Готово",()=>{manager.Change(id);ShowModules();}),Button("Удалить",()=>{manager.Change(id,true);ShowModules();}));
            }
        }
    }
    private void BuildWeather(StackPanel page,JsonObject settings)
    {
        var city=Input("Город для погоды","",120);Field(page,"Поиск города",city);
        var results=new StackPanel();var status=Status();page.Children.Add(status);
        page.Children.Add(AsyncButton("Найти город",async()=>
        {
            status.Text="Поиск…";results.Children.Clear();
            try
            {
                var found=await new WeatherModule(data).Search(city.Text);
                if(!page.IsLoaded)return;
                status.Text=found.Length==0?"Город не найден. Уточните название.":"Выберите город:";
                foreach(var option in found)results.Children.Add(Button(option.Name,()=>
                {
                    var value=FeatureFiles.Object(data,"modules.json");value["city"]=new JsonObject{["name"]=option.Name,["latitude"]=option.Latitude,["longitude"]=option.Longitude};FeatureFiles.Write(data,"modules.json",value);ShowModules();
                }));
            }
            catch(Exception error){status.Text=error.Message;}
        }));page.Children.Add(results);
        if(settings["city"] is not JsonObject selected)return;
        page.Children.Add(Text(selected["name"]!.ToString(),"Accent",15));
        var weather=Text("Загрузка погоды…");page.Children.Add(weather);
        async Task Update(bool force)
        {
            try
            {
                var reading=await new WeatherModule(data).Read(force);
                if(!page.IsLoaded)return;
                weather.Text=reading is null?"Выберите город.":$"{reading.Temperature:+0.#;-0.#;0} °C · {WeatherModule.Description(reading.Code)}\nВетер {reading.Wind:0.#} м/с\nОбновлено {reading.Updated.LocalDateTime:dd.MM HH:mm}"+(reading.Status.Length>0?"\n"+reading.Status:"");
            }
            catch(Exception error){weather.Text=error.Message;}
        }
        page.Children.Add(AsyncButton("Обновить погоду",()=>Update(true)));
        page.Children.Add(Button("Данные Open-Meteo",()=>Process.Start(new ProcessStartInfo("https://open-meteo.com/"){UseShellExecute=true})));
        page.Loaded+=async(_,_)=>await Update(false);
    }
}
