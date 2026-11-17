using System.Text;

namespace PosterPrintRequest.TdxValidator;

public static class MaskedInput
{
    public static string ReadPassword()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (key.KeyChar == '\0')
                continue;

            buffer.Append(key.KeyChar);
            Console.Write('*');
        }

        return buffer.ToString();
    }
}
