using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Presenter;
using Xunit;

namespace ReaRadioDj.Tests;

public class ControlRoomTests
{
    [Fact]
    public void The_weather_answer_becomes_degrees_sky_beaufort_max_and_rain()
    {
        using var doc = JsonDocument.Parse("""
            {"current":{"temperature_2m":7.6,"weather_code":61,"wind_speed_10m":6.1},
             "daily":{"temperature_2m_max":[9.4],"precipitation_probability_max":[80]}}
            """);
        var w = BreakExtras.ParseWeather(doc.RootElement);
        Assert.Equal(new WeatherReport(8, 61, "regen", 4, 9, 80), w);

        using var noRain = JsonDocument.Parse("""
            {"current":{"temperature_2m":-0.4,"weather_code":0,"wind_speed_10m":0.1},
             "daily":{"temperature_2m_max":[2.0],"precipitation_probability_max":[null]}}
            """);
        Assert.Equal(new WeatherReport(0, 0, "zonnig", 0, 2, -1), BreakExtras.ParseWeather(noRain.RootElement));
    }

    [Fact]
    public void The_weather_url_uses_the_place_of_the_station()
    {
        var url = BreakExtras.WeatherUrl(new WeatherOptions { Latitude = 53.006, Longitude = 6.763 });
        Assert.StartsWith("https://api.open-meteo.com/v1/forecast?latitude=53.006&longitude=6.763&", url);
    }
}
