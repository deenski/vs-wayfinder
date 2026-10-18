using System;

namespace Wayfinding.Infra;

public static class PlayerNameParser
{
    public const int MaxLength = 64;

    public const string Usage = "Use .nav player <name>.";

    public static bool TryParse(string? text, out string name, out string error)
    {
        name = (text ?? "").Trim();
        if (name.Length == 0)
        {
            error = Usage;
            return false;
        }

        if (name.Length > MaxLength)
        {
            name = "";
            error = "That player name is too long.";
            return false;
        }

        foreach (var ch in name)
        {
            if (char.IsControl(ch))
            {
                name = "";
                error = "That player name is not valid.";
                return false;
            }
        }

        error = "";
        return true;
    }
}
