/*
  keypaste addition to the vendored KeePassLib, compiled only under KEYPASTE_TWOFISH.
  See third_party/KeePassLib/UPSTREAM.md.
*/

#if KEYPASTE_TWOFISH
using System;
using System.IO;
using System.Security.Cryptography;

using KeePassLib.Resources;
using KeePassLib.Utility;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace KeePassLib.Cryptography.Cipher
{
	/// <summary>
	/// Twofish as KeePass's TwofishCipher plugin and KeePassXC write it into a
	/// KDBX file: a 256-bit key, CBC mode with a 128-bit IV and PKCS#7 padding.
	/// The block cipher, the mode and the padding are BouncyCastle's, vendored
	/// in third_party/bouncycastle-twofish; this class only moves whole blocks
	/// through them.
	/// </summary>
	public sealed class TwofishCipherEngine : ICipherEngine
	{
		private static readonly PwUuid g_uuid = new PwUuid(new byte[] {
			0xAD, 0x68, 0xF2, 0x9F, 0x57, 0x6F, 0x4B, 0xB9,
			0xA3, 0x6A, 0xD4, 0x7A, 0xF9, 0x65, 0x34, 0x6C });

		/// <summary>
		/// The cipher UUID KeePass's TwofishCipher plugin and KeePassXC write
		/// in the outer header.
		/// </summary>
		public static PwUuid TwofishUuid
		{
			get { return g_uuid; }
		}

		public PwUuid CipherUuid
		{
			get { return g_uuid; }
		}

		public string DisplayName
		{
			get
			{
				return ("Twofish (" + KLRes.KeyBits.Replace("{PARAM}", "256") + ")");
			}
		}

		public Stream EncryptStream(Stream s, byte[] pbKey, byte[] pbIV)
		{
			return CreateStream(s, true, pbKey, pbIV);
		}

		public Stream DecryptStream(Stream s, byte[] pbKey, byte[] pbIV)
		{
			return CreateStream(s, false, pbKey, pbIV);
		}

		private static Stream CreateStream(Stream s, bool bEncrypt, byte[] pbKey, byte[] pbIV)
		{
			if(s == null) throw new ArgumentNullException("s");
			if(pbKey == null) throw new ArgumentNullException("pbKey");
			if(pbKey.Length != 32) throw new ArgumentOutOfRangeException("pbKey");
			if(pbIV == null) throw new ArgumentNullException("pbIV");
			if(pbIV.Length != TwofishCbcTransform.BlockSize) throw new ArgumentOutOfRangeException("pbIV");
			if(bEncrypt && !s.CanWrite) throw new ArgumentException("Stream must be writable!");
			if(!bEncrypt && !s.CanRead) throw new ArgumentException("Stream must be readable!");

			TwofishCbcTransform t = new TwofishCbcTransform(bEncrypt, pbKey, pbIV);
			try
			{
				return new CryptoStreamEx(s, t, (bEncrypt ? CryptoStreamMode.Write :
					CryptoStreamMode.Read), null);
			}
			catch(Exception) { t.Dispose(); throw; }
		}
	}

	/// <summary>
	/// Whole blocks through BouncyCastle's CBC over Twofish, the last one padded
	/// or unpadded by its PKCS#7. Decryption holds the newest block back, because
	/// only the end of the stream says it is the padded one.
	/// </summary>
	internal sealed class TwofishCbcTransform : ICryptoTransform
	{
		internal const int BlockSize = 16;

		private readonly bool m_bEncrypt;
		private readonly TwofishEngine m_twofish = new TwofishEngine();
		private readonly KeyParameter m_key;
		private readonly CbcBlockCipher m_cbc;
		private readonly Pkcs7Padding m_padding = new Pkcs7Padding();

		private readonly byte[] m_pbBlock = new byte[BlockSize];
		private readonly byte[] m_pbHeld = new byte[BlockSize];
		private bool m_bHolding = false;
		private bool m_bDisposed = false;

		public TwofishCbcTransform(bool bEncrypt, byte[] pbKey, byte[] pbIV)
		{
			m_bEncrypt = bEncrypt;
			m_key = new KeyParameter(pbKey);
			m_cbc = new CbcBlockCipher(m_twofish);
			m_cbc.Init(bEncrypt, new ParametersWithIV(m_key, pbIV));
		}

		public int InputBlockSize
		{
			get { return BlockSize; }
		}

		public int OutputBlockSize
		{
			get { return BlockSize; }
		}

		public bool CanTransformMultipleBlocks
		{
			get { return true; }
		}

		public bool CanReuseTransform
		{
			get { return false; }
		}

		public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
			byte[] outputBuffer, int outputOffset)
		{
			CheckRange(inputBuffer, inputOffset, inputCount);
			if((inputCount % BlockSize) != 0)
				throw new ArgumentException("Not a whole number of blocks.", "inputCount");
			if(outputBuffer == null) throw new ArgumentNullException("outputBuffer");

			int cbOut = 0;
			try
			{
				for(int i = 0; i < inputCount; i += BlockSize)
				{
					// Read before anything is written, so an output buffer that is the
					// input buffer is only ever written behind what has been read.
					Array.Copy(inputBuffer, inputOffset + i, m_pbBlock, 0, BlockSize);

					if(m_bEncrypt)
					{
						m_cbc.ProcessBlock(m_pbBlock, 0, outputBuffer, outputOffset + cbOut);
						cbOut += BlockSize;
						continue;
					}

					if(m_bHolding)
					{
						Array.Copy(m_pbHeld, 0, outputBuffer, outputOffset + cbOut, BlockSize);
						cbOut += BlockSize;
					}

					m_cbc.ProcessBlock(m_pbBlock, 0, m_pbHeld, 0);
					m_bHolding = true;
				}
			}
			finally { MemUtil.ZeroByteArray(m_pbBlock); }

			return cbOut;
		}

		public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
		{
			CheckRange(inputBuffer, inputOffset, inputCount);

			int cbWhole = inputCount - (inputCount % BlockSize);
			byte[] pbOut = new byte[cbWhole + BlockSize];
			try
			{
				int cbOut = ((cbWhole > 0) ? TransformBlock(inputBuffer, inputOffset,
					cbWhole, pbOut, 0) : 0);

				if(m_bEncrypt)
				{
					Array.Copy(inputBuffer, inputOffset + cbWhole, m_pbBlock, 0, inputCount - cbWhole);
					m_padding.AddPadding(m_pbBlock, inputCount - cbWhole);
					m_cbc.ProcessBlock(m_pbBlock, 0, pbOut, cbOut);
					cbOut += BlockSize;
				}
				else
				{
					if((cbWhole != inputCount) || !m_bHolding)
						throw new CryptographicException("The Twofish data is not a whole number of blocks.");

					int cbPad;
					try { cbPad = m_padding.PadCount(m_pbHeld); }
					catch(InvalidCipherTextException ex)
					{
						throw new CryptographicException("The Twofish padding is invalid.", ex);
					}

					Array.Copy(m_pbHeld, 0, pbOut, cbOut, BlockSize - cbPad);
					cbOut += BlockSize - cbPad;
				}

				byte[] pbResult = new byte[cbOut];
				Array.Copy(pbOut, pbResult, cbOut);
				return pbResult;
			}
			finally
			{
				MemUtil.ZeroByteArray(pbOut);
				MemUtil.ZeroByteArray(m_pbBlock);
				MemUtil.ZeroByteArray(m_pbHeld);
				m_bHolding = false;
			}
		}

		public void Dispose()
		{
			if(m_bDisposed) return;

			m_twofish.Clear();
			m_key.Clear();
			MemUtil.ZeroByteArray(m_pbBlock);
			MemUtil.ZeroByteArray(m_pbHeld);
			m_bHolding = false;
			m_bDisposed = true;
		}

		private void CheckRange(byte[] pb, int iOffset, int cb)
		{
			if(m_bDisposed) throw new ObjectDisposedException("TwofishCbcTransform");
			if(pb == null) throw new ArgumentNullException("inputBuffer");
			if((iOffset < 0) || (cb < 0) || (cb > (pb.Length - iOffset)))
				throw new ArgumentOutOfRangeException("inputCount");
		}
	}
}
#endif
