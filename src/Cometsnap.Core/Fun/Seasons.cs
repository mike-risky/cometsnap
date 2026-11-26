namespace Cometsnap.Core.Fun;

public enum Season { None, Halloween, Winter }

/// <summary>Which seasonal skin applies on a given day.</summary>
public static class Seasons
{
    /// <summary>Halloween from October 18 to 31, snow for all of December.</summary>
    public static Season For(DateTime day) => day.Month switch
    {
        10 when day.Day >= 18 => Season.Halloween,
        12 => Season.Winter,
        _ => Season.None,
    };
}
