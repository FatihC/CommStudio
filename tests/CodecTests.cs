using System;
using CommStudio;

internal static class CodecTests
{
    private static int failures;

    private static void Main()
    {
        TestAsciiTokens();
        TestAsciiRendering();
        TestHexFormats();
        TestHexValidation();

        if (failures != 0)
        {
            Console.Error.WriteLine(failures + " codec test(s) failed.");
            Environment.Exit(1);
        }

        Console.WriteLine("Codec tests passed.");
    }

    private static void TestAsciiTokens()
    {
        byte[] actual = ByteCodec.ParseAsciiCommand("A[ack][CR][LF][80][0xFF]");
        AssertBytes("ASCII named and byte tokens", new byte[] { 0x41, 0x06, 0x0D, 0x0A, 0x80, 0xFF }, actual);

        actual = ByteCodec.ParseAsciiCommand("[unknown]");
        AssertBytes("Unknown tokens remain literal", new byte[] { 0x5B, 0x75, 0x6E, 0x6B, 0x6E, 0x6F, 0x77, 0x6E, 0x5D }, actual);
    }

    private static void TestAsciiRendering()
    {
        string actual = ByteCodec.ToAscii(new byte[] { 0x41, 0x06, 0x0D, 0x0A, 0x7F, 0x80, 0xFF });
        AssertEqual("ASCII rendering", "A[ACK][CR][LF][DEL][80][FF]", actual);
        AssertEqual("Hex rendering", "41 06 0D 0A 7F 80 FF", ByteCodec.ToHex(new byte[] { 0x41, 0x06, 0x0D, 0x0A, 0x7F, 0x80, 0xFF }));
    }

    private static void TestHexFormats()
    {
        byte[] expected = new byte[] { 0x06, 0x41, 0x0D };
        AssertBytes("Spaced Hex", expected, ByteCodec.ParseHexCommand("06 41 0D"));
        AssertBytes("Compact Hex", expected, ByteCodec.ParseHexCommand("06410D"));
        AssertBytes("Prefixed Hex", expected, ByteCodec.ParseHexCommand("0x06 0x41 0x0D"));
    }

    private static void TestHexValidation()
    {
        AssertThrows("Odd Hex digits", delegate { ByteCodec.ParseHexCommand("0A B"); });
        AssertThrows("Invalid Hex character", delegate { ByteCodec.ParseHexCommand("0A Q1"); });
    }

    private static void AssertBytes(string name, byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length)
        {
            Fail(name, "length " + actual.Length + " instead of " + expected.Length);
            return;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                Fail(name, "byte " + i + " was " + actual[i].ToString("X2") + " instead of " + expected[i].ToString("X2"));
                return;
            }
        }
    }

    private static void AssertEqual(string name, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Fail(name, "was '" + actual + "' instead of '" + expected + "'");
        }
    }

    private static void AssertThrows(string name, Action action)
    {
        try
        {
            action();
            Fail(name, "did not throw FormatException");
        }
        catch (FormatException)
        {
        }
    }

    private static void Fail(string name, string message)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + message);
    }
}
