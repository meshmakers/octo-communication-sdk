using System.Globalization;
using System.Text.Json.Nodes;
using Bogus;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.SimulationNodes.Generators;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Sdk.SimulationNodes.Services;

/// <inheritdoc />
public class Simulator : ISimulator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Faker _faker;
    private Person? _person;

    internal Simulator(string locale, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _faker = new Faker(locale);
    }

    /// <inheritdoc />
    public object? Generate(string simulatorKey, IEtlContext etlContext, JsonObject config)
    {
        switch (simulatorKey)
        {
            case "Energy.MeteringPointNumber":
                var length = config.GetValue("length", 31);
                var countryCode = config.GetValue<string>("countryCode", "AT");
                var maxValue = Helpers.GetMaxValueForDigitCount(length < 19 ? length : 19);

                var counter = _faker.Random.ULong(maxValue);
                string numberPart = counter.ToString().PadLeft(length, '0');
                return countryCode + numberPart;
            case "Energy.DateTime":
                var startDateString = config.GetValue("startDate", DateTime.UtcNow.ToString("o"));
                var endDateString = config.GetValue("endDate", DateTime.UtcNow.AddDays(1).ToString("o"));
                var startDate = DateTime.Parse(startDateString, null, DateTimeStyles.RoundtripKind);
                var endDate = DateTime.Parse(endDateString, null, DateTimeStyles.RoundtripKind);
                return _faker.Date.Between(startDate, endDate);
            case "Address.Country":
                return _faker.Address.County();
            case "Address.CountryCode":
                return _faker.Address.CountryCode();
            case "Address.ZipCode":
                return _faker.Address.ZipCode();
            case "Address.City":
                return _faker.Address.City();
            case "Address.StreetAddress":
                return _faker.Address.StreetAddress();
            case "Address.StreetName":
                return _faker.Address.StreetName();
            case "Address.BuildingNumber":
                return _faker.Address.BuildingNumber();
            case "Person.FirstName":
                return GetPerson().FirstName;
            case "Person.LastName":
                return GetPerson().LastName;
            case "Person.FullName":
                return GetPerson().FullName;
            case "Person.Email":
                return GetPerson().Email;
            case "Person.UserName":
                return GetPerson().UserName;
            case "Person.Avatar":
                return GetPerson().Avatar;
            case "Person.Phone":
                return GetPerson().Phone;
            case "Person.Company.Name":
                return GetPerson().Company.Name;
            case "Person.Company.CatchPhrase":
                return GetPerson().Company.CatchPhrase;
            case "Person.Company.Bs":
                return GetPerson().Company.Bs;
            case "Person.Address.Street":
                return GetPerson().Address.Street;
            case "Person.Address.State":
                return GetPerson().Address.State;
            case "Person.Address.Suite":
                return GetPerson().Address.Suite;
            case "Person.Address.Geo.Lat":
                return GetPerson().Address.Geo.Lat;
            case "Person.Address.Geo.Lng":
                return GetPerson().Address.Geo.Lng;
            case "Person.Address.City":
                return GetPerson().Address.City;
            case "Person.Address.ZipCode":
                return GetPerson().Address.ZipCode;
            case "Person.DateOfBirth":
                return GetPerson().DateOfBirth;
            case "Person.Gender":
                return GetPerson().Gender;
            default:
                var generator = _serviceProvider.GetKeyedService<IValueGenerator>(simulatorKey);
                if (generator == null)
                {
                    throw new PipelineNodeExecutionException("No generator found for key: " + simulatorKey);
                }
                return generator.Generate(etlContext, _faker, config);
        }
    }

    private Person GetPerson()
    {
        return _person ??= new Person(_faker.Locale, _faker.Random.Number(1, 100000), _faker.Date.Past());
    }
}
