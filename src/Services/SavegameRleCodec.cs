namespace Console2Lce;

public static class SavegameRleCodec
{
    public static byte[] Decode(ReadOnlySpan<byte> encodedBytes, int expectedSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedSize);

        byte[] output = DecodeCore(encodedBytes, expectedSize, out int inputConsumed, out int outputProduced);
        if (outputProduced != expectedSize)
        {
            throw new SavegameDatDecompressionFailedException(
                $"RLE decode produced {outputProduced} bytes, expected {expectedSize}.");
        }

        return output;
    }

    public static bool TryDecodeExact(ReadOnlySpan<byte> encodedBytes, int expectedSize, out byte[] output)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedSize);

        output = DecodeCore(encodedBytes, expectedSize, out int inputConsumed, out int outputProduced);
        return outputProduced == expectedSize && inputConsumed == encodedBytes.Length;
    }

    public static bool TryDecodePrefix(ReadOnlySpan<byte> encodedBytes, int expectedSize, out byte[] output, out int inputConsumed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedSize);

        output = DecodeCore(encodedBytes, expectedSize, out inputConsumed, out int outputProduced);
        if (outputProduced != expectedSize)
        {
            output = Array.Empty<byte>();
            inputConsumed = 0;
            return false;
        }

        return true;
    }

    private static byte[] DecodeCore(ReadOnlySpan<byte> encodedBytes, int expectedSize, out int inputConsumed, out int outputProduced)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedSize);

        byte[] output = new byte[expectedSize];
        int inputOffset = 0;
        int outputOffset = 0;

        while (inputOffset < encodedBytes.Length && outputOffset < expectedSize)
        {
            byte value = encodedBytes[inputOffset++];
            if (value != 0xFF)
            {
                output[outputOffset++] = value;
                continue;
            }

            if (inputOffset >= encodedBytes.Length)
            {
                break;
            }

            int count = encodedBytes[inputOffset++];
            if (count < 3)
            {
                count++;
                for (int index = 0; index < count && outputOffset < expectedSize; index++)
                {
                    output[outputOffset++] = 0xFF;
                }

                continue;
            }

            count++;
            if (inputOffset >= encodedBytes.Length)
            {
                break;
            }

            byte repeatedValue = encodedBytes[inputOffset++];
            for (int index = 0; index < count && outputOffset < expectedSize; index++)
            {
                output[outputOffset++] = repeatedValue;
            }
        }

        inputConsumed = inputOffset;
        outputProduced = outputOffset;
        return output;
    }
}
