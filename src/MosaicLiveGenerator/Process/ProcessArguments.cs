using System.Text;

namespace MosaicLiveGenerator.Process;

// net472 has no ProcessStartInfo.ArgumentList. This reproduces the Windows
// command-line escaping the runtime applies on modern frameworks so individual
// arguments survive intact in the single Arguments string.
internal static class ProcessArguments
{
    public static string ToCommandLine(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        foreach (var arg in args)
            AppendArgument(sb, arg);
        return sb.ToString();
    }

    private static void AppendArgument(StringBuilder sb, string argument)
    {
        if (sb.Length != 0)
            sb.Append(' ');

        if (argument.Length != 0 && ContainsNoSpecialCharacters(argument))
        {
            sb.Append(argument);
            return;
        }

        sb.Append('"');
        int i = 0;
        while (i < argument.Length)
        {
            char c = argument[i++];
            if (c == '\\')
            {
                int backslashes = 1;
                while (i < argument.Length && argument[i] == '\\')
                {
                    i++;
                    backslashes++;
                }

                if (i == argument.Length)
                    sb.Append('\\', backslashes * 2);
                else if (argument[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    i++;
                }
                else
                    sb.Append('\\', backslashes);
            }
            else if (c == '"')
            {
                sb.Append('\\');
                sb.Append('"');
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append('"');
    }

    private static bool ContainsNoSpecialCharacters(string s)
    {
        foreach (var c in s)
            if (char.IsWhiteSpace(c) || c == '"')
                return false;
        return true;
    }
}
