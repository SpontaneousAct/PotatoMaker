using System.Text.Json.Serialization;

namespace PotatoMaker.Core;

/// <summary>
/// Controls whether the encoded video keeps the source frame rate or caps it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EncodeFrameRateMode>))]
public enum EncodeFrameRateMode
{
    Original = 0,
    Fps30 = 30,
    Fps60 = 60
}
