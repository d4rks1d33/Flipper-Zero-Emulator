// Telenor Inpli TFTP Server Module
//
// Copyright 2018 Telenor Inpli AS Norway

using System;

internal static class BufferPrimitives
{
    /// <summary>
    /// Read a 16-bit big endian value from a buffer at a given offset
    /// </summary>
    /// <param name="buffer">The buffer to read from</param>
    /// <param name="offset">The offset in the buffer</param>
    /// <returns>A 16 bit value</returns>
    /// <exception cref="IndexOutOfRangeException">If reading past the end of the buffer</exception>
    public static int Get16BE(this byte [] buffer, long offset)
    {
        if ((offset + 2) >= buffer.Length)
            throw new IndexOutOfRangeException();

        return ((int)(buffer[offset]) << 8) | (int)buffer[offset+1];
    }

    /// <summary>
    /// Writes a 16-bit big endian to a buffer at a given offset
    /// </summary>
    /// <param name="buffer">The buffer to write into</param>
    /// <param name="offset">The offset within the buffer to write to</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="IndexOutOfRangeException">If attempting to write past the end</exception>
    public static void Write16BE(this byte [] buffer, long offset, int value)
    {
        if ((offset + 2) >= buffer.Length)
            throw new IndexOutOfRangeException();

        buffer[offset] = (byte)((value >> 8) & 0xff);
        buffer[offset + 1] = (byte)(value & 0xff);
    }
}
