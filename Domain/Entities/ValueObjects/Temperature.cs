namespace Domain.Entities.ValueObjects;

public readonly record struct Temperature(double Celsius)
{
    public double Fahrenheit => 32 + (Celsius * 9 / 5);
    public double Kelvin => Celsius + 273.15;

    public static Temperature FromCelsius(double celsius) => new(celsius);
    public static Temperature FromFahrenheit(double fahrenheit) => new((fahrenheit - 32) * 5 / 9);
    public static Temperature FromKelvin(double kelvin) => new(kelvin - 273.15);

    public override string ToString() => $"{Celsius:F1}°C";
}