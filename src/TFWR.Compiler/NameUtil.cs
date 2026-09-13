using System.Text;

namespace TFWR.Compiler;

internal static class NameUtil
{
    public static string PascalToSnake(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                var prev = name[i - 1];
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                if (!char.IsUpper(prev) || (next != '\0' && char.IsLower(next)))
                {
                    sb.Append('_');
                }
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    public static string SanitizeIdentifier(string name)
    {
        if (name is "None" or "True" or "False" or "and" or "or" or "not" or "in" or "is" or "def" or "class" or "return" or "for" or "while" or "if" or "elif" or "else" or "break" or "continue" or "pass" or "global" or "import" or "from" or "as")
        {
            return name + "_";
        }

        return name;
    }

    public static string ModuleNameFromPath(string relativePath)
    {
        return Path.GetFileNameWithoutExtension(relativePath);
    }

    public static string OutputPyPath(string relativeCsPath)
    {
        return Path.ChangeExtension(relativeCsPath.Replace('\\', '/'), ".py");
    }
}
