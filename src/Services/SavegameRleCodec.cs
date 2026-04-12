namespace Console2Lce;

public static class SavegameRleCodec
{
    public static byte[] Decode(ReadOnlySpan<byte> encodedBytes, int expectedSize)
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

        if (outputOffset != expectedSize)
        {
            throw new SavegameDatDecompressionFailedException(
                $"RLE decode produced {outputOffset} bytes, expected {expectedSize}.");
        }

        return output;
    }
}
