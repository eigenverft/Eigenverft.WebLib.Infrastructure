using System;
using System.Linq;
using System.Text;

using Eigenverft.WebLib.Infrastructure.Text;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class Base92JsonSafeEncoderTests
{
    [TestMethod]
    public void AlphabetMatchesJsonSafeSpecification()
    {
        Assert.AreEqual(92, Base92JsonSafeEncoder.Alphabet.Length);
        Assert.AreEqual(92, Base92JsonSafeEncoder.Alphabet.Distinct().Count());
        Assert.IsFalse(Base92JsonSafeEncoder.Alphabet.Contains(' '));
        Assert.IsFalse(Base92JsonSafeEncoder.Alphabet.Contains('"'));
        Assert.IsFalse(Base92JsonSafeEncoder.Alphabet.Contains('\\'));

        foreach (char character in Base92JsonSafeEncoder.Alphabet)
        {
            Assert.IsTrue(character is >= '!' and <= '~');
        }
    }

    [TestMethod]
    public void KnownVectorsAreCanonical()
    {
        Assert.AreEqual(string.Empty, Base92JsonSafeEncoder.Encode(Array.Empty<byte>()));
        Assert.AreEqual("!", Base92JsonSafeEncoder.Encode(new byte[] { 0 }));
        Assert.AreEqual("!!#", Base92JsonSafeEncoder.Encode(new byte[] { 0, 0, 1 }));
        Assert.AreEqual("Q2Aeq)", Base92JsonSafeEncoder.Encode(Encoding.ASCII.GetBytes("Hello")));
    }

    [TestMethod]
    public void RoundTripPreservesArbitraryBytesAndLeadingZeros()
    {
        byte[] original =
        {
            0, 0, 0, 1, 2, 3, 4, 5, 127, 128, 254, 255,
        };

        string encoded = Base92JsonSafeEncoder.Encode(original);
        byte[] decoded = Base92JsonSafeEncoder.Decode(encoded);

        CollectionAssert.AreEqual(original, decoded);
        Assert.IsFalse(encoded.Contains(' '));
        Assert.IsFalse(encoded.Contains('"'));
        Assert.IsFalse(encoded.Contains('\\'));
    }

    [TestMethod]
    public void TryDecodeRejectsCharactersOutsideAlphabet()
    {
        Assert.IsFalse(Base92JsonSafeEncoder.TryDecode("abc def", out _));
        Assert.IsFalse(Base92JsonSafeEncoder.TryDecode("abc\"def", out _));
        Assert.IsFalse(Base92JsonSafeEncoder.TryDecode("abc\\def", out _));
        Assert.IsFalse(Base92JsonSafeEncoder.TryDecode(null, out _));
    }
}
