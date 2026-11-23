using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Sputnik;

internal sealed record WeatherCity(string Name,double Latitude,double Longitude);
internal sealed record WeatherReading(double Temperature,double Wind,int Code,DateTimeOffset Updated,bool Cached,string Status);
internal sealed class WeatherModule(string directory, HttpClient? client=null)
{
    private static readonly HttpClient Shared=new() {Timeout=TimeSpan.FromSeconds(12)};
    private HttpClient Http=>client??Shared;
    internal async Task<WeatherCity[]> Search(string name)
    {
        if(name.Trim().Length is < 2 or > 120)throw new InvalidOperationException("Введите название города от 2 до 120 символов.");
        var node=await Get("https://geocoding-api.open-meteo.com/v1/search?name="+Uri.EscapeDataString(name.Trim())+"&count=5&language=ru&format=json");
        return (node["results"] as JsonArray ?? new()).Select(item=>new WeatherCity(item!["name"]!.ToString()+", "+item["country"]+ (item["admin1"] is {} region ? ", "+region : ""),item["latitude"]!.GetValue<double>(),item["longitude"]!.GetValue<double>())).Where(Valid).ToArray();
    }
    internal static bool Valid(WeatherCity city)=>city.Name.Length is >0 and <=400 && !double.IsNaN(city.Latitude) && !double.IsNaN(city.Longitude) && city.Latitude is >=-90 and <=90 && city.Longitude is >=-180 and <=180;
    internal async Task<WeatherReading?> Read(bool force=false)
    {
        var settings=FeatureFiles.Object(directory,"modules.json");
        if(settings["weather"]?.GetValue<bool>()!=true || settings["city"] is not JsonObject city)return null;
        var location=new WeatherCity(city["name"]!.ToString(),city["latitude"]!.GetValue<double>(),city["longitude"]!.GetValue<double>());
        if(!Valid(location))throw new InvalidOperationException("Выберите город заново.");
        string key=location.Latitude.ToString("R",CultureInfo.InvariantCulture)+","+location.Longitude.ToString("R",CultureInfo.InvariantCulture);
        var cache=FeatureFiles.Object(directory,"weather-cache.json");
        WeatherReading? Cached(string status)
        {
            if(cache["key"]?.ToString()!=key || cache["updated"] is null)return null;
            return new(cache["temperature"]!.GetValue<double>(),cache["wind"]!.GetValue<double>(),cache["code"]!.GetValue<int>(),DateTimeOffset.FromUnixTimeMilliseconds(cache["updated"]!.GetValue<long>()),true,status);
        }
        var previous=Cached("");if(!force && previous is not null && DateTimeOffset.UtcNow-previous.Updated<TimeSpan.FromMinutes(30))return previous;
        try
        {
            var response=await Get("https://api.open-meteo.com/v1/forecast?latitude="+location.Latitude.ToString("R",CultureInfo.InvariantCulture)+"&longitude="+location.Longitude.ToString("R",CultureInfo.InvariantCulture)+"&current=temperature_2m,weather_code,wind_speed_10m&wind_speed_unit=ms&timezone=auto&forecast_days=1");
            var current=response["current"] ?? throw new InvalidOperationException("Сервис не вернул текущую погоду.");
            double temperature=current["temperature_2m"]!.GetValue<double>(),wind=current["wind_speed_10m"]!.GetValue<double>();int code=current["weather_code"]!.GetValue<int>();
            if(double.IsNaN(temperature)||temperature is < -100 or > 70 || double.IsNaN(wind)||wind is < 0 or > 150 || code is <0 or >99)throw new InvalidOperationException("Сервис вернул некорректную погоду.");
            var now=DateTimeOffset.UtcNow;
            // Do not replace the new city's cache when an older request finishes late.
            if(FolderSync.Canonical(FeatureFiles.Object(directory,"modules.json")["city"])==FolderSync.Canonical(city))
                FeatureFiles.Write(directory,"weather-cache.json",new JsonObject{["key"]=key,["updated"]=now.ToUnixTimeMilliseconds(),["temperature"]=temperature,["wind"]=wind,["code"]=code});
            return new(temperature,wind,code,now,false,"");
        }
        catch(Exception error) when(error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or NullReferenceException)
        {return Cached("Нет связи. Показаны сохранённые данные.")??throw new InvalidOperationException("Не удалось загрузить погоду. Проверьте подключение и попробуйте ещё раз.",error);}
    }
    private async Task<JsonObject> Get(string url)
    {
        using var response=await Http.GetAsync(url);response.EnsureSuccessStatusCode();
        string text=await response.Content.ReadAsStringAsync();if(text.Length>1_000_000)throw new InvalidOperationException("Слишком большой ответ сервиса погоды.");
        return JsonNode.Parse(text) as JsonObject ?? throw new InvalidOperationException("Неверный ответ сервиса погоды.");
    }
    internal static string Description(int code)=>code switch{0=>"Ясно",1 or 2=>"Переменная облачность",3=>"Пасмурно",45 or 48=>"Туман",>=51 and <=57=>"Морось",>=61 and <=67=>"Дождь",>=71 and <=77=>"Снег",>=80 and <=82=>"Ливень",85 or 86=>"Снегопад",>=95=>"Гроза",_=>"Погода"};
}
