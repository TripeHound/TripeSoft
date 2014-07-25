namespace TripeSoft.Stream
{
	using System;
	//using System.Collections.Generic;
	//using System.Linq;
	//using System.Text;
	using System.IO;

	//GLH
	//	Not sure whether 'StreamChunk' should derive from 'Stream' or just have one as a private member.
	//	Deriving gives us the pre-defined interface (and, from the description, we're inheriting some
	//	virtual behaviour), but we're not really "using" the base class (I think) ... all calls to "do"
	//	something are fired through the private member.
	//
	//	Despite my reservations, it seems at least one other things deriving is the right way; see:
	//		http://www.java2s.com/Code/CSharp/File-Stream/BufferedInputStream.htm
	//

	/// <summary>
	/// Encapsulates a generic <seealso cref="Stream"/> to provide access to a defined 'chunk' of
	/// the underlying stream, preventing reading, writing and seeking outside the defined limits.
	/// </summary>
	public class StreamChunk : Stream
	{
		private Stream	underlyingStream;
		private long	underlyingOffset;
		private long	chunkLength;

		/// <summary>
		/// Creates a new StreamChunk giving access to a limited portion of the underlying stream, specified by position and length.
		/// </summary>
		/// <param name="stream">Underlying stream. Must be seekable.</param>
		/// <param name="offset">Location of chunk in underlying stream.</param>
		/// <param name="length">Length of chunk.</param>
		/// <exception cref="NotSupportedException">The underlying stream is not seekable.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Specified chunk extends beyond the length of the underlying stream.</exception>
		public StreamChunk( Stream stream, long offset, long length )
		{
			if( !stream.CanSeek )
				throw new NotSupportedException( "Underlynig stream must be seekable" );
			if( offset+length > stream.Length )
				throw new ArgumentOutOfRangeException( String.Format( "Chunk at {0} of length {1} exceeds length of underlying stream ({2})", offset, length, stream.Length ) );
			underlyingStream = stream;
			underlyingOffset = offset;
			chunkLength = length;
			Position = 0;
		}

		/// <summary>
		/// Creates a new StreamChunk giving access to a limited portion of the underlying stream, starting at the current position of the given length.
		/// </summary>
		/// <param name="stream">Underlying stream. Must be seekable.</param>
		/// <param name="length">Length of chunk.</param>
		/// <exception cref="NotSupportedException">The underlying stream is not seekable.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Specified chunk extends beyond the length of the underlying stream.</exception>
		public StreamChunk( Stream stream, long length )
			: this( stream, stream.Position, length )
		{
		}

		/// <summary>
		/// Gets or sets the position within the underlying stream relative to the chunk.
		/// </summary>
		/// <exception cref="IOException">Position of underlying stream not within chunk.</exception>
		public override long Position
		{
			get
			{
				long posn = underlyingStream.Position - underlyingOffset ;
				if( posn < 0 || posn >= chunkLength )
					throw new IOException( "Underlying stream not positioned within chunk" );
				return posn;
			}
			set
			{
				Seek( value, SeekOrigin.Begin ) ;
			}
		}

		/// <summary>
		/// Gets the length of the chunk.
		/// </summary>
		public override long Length
		{
			get{ return chunkLength; }
		}

		/// <summary>
		/// Indicates whether the underlying stream can be read.
		/// </summary>
		public override bool CanRead
		{
			get { return underlyingStream.CanRead; }
		}

		/// <summary>
		/// Indicates whether the underlying stream can be written.
		/// </summary>
		public override bool CanWrite
		{
			get { return underlyingStream.CanWrite; }
		}

		/// <summary>
		/// Always true (a StreamChunk cannot be created from a stream that does not support seeking).
		/// </summary>
		public override bool CanSeek
		{
			get { return true; }
		}

		/// <summary>
		/// Reads 'count' bytes from the current position of the underlying stream into 'buffer' at 'offset', provided the bytes fall within the chunk.
		/// </summary>
		/// <param name="buffer">Byte-array in which to store the data.</param>
		/// <param name="offset">Offset within 'buffer' to store the data.</param>
		/// <param name="count">Number of bytes to read.</param>
		/// <returns>Number of bytes actually read.</returns>
		/// <exception cref="ArgumentException">Data to be read would extend beyond the chunk.	</exception>
		public override int Read( byte[] buffer, int offset, int count )
		{
//				if( !CanRead )
//					throw new NotImplementedException( "Underlying stream does not support reading" );
			if( Position + count > chunkLength )
				throw new ArgumentException( );
			return underlyingStream.Read( buffer, offset, count );
		}

		/// <summary>
		/// Writes 'count' bytes from position 'offset' of 'buffer' to the current position of the underlying stream, provided the bytes fall within the chunk.
		/// </summary>
		/// <param name="buffer">Byte-array from which to write the data.</param>
		/// <param name="offset">Offset within 'buffer' of the data to write.</param>
		/// <param name="count">Number of bytes to write.</param>
		/// <exception cref="ArgumentException">Data to be written would extend beyond the chunk.</exception>
		public override void Write( byte[] buffer, int offset, int count )
		{
//				if( !CanWrite )
//					throw new NotImplementedException( "Underlying stream does not support writing" );
			if( Position + count > chunkLength )
				throw new ArgumentException();
			underlyingStream.Write( buffer, offset, count );
		}

		/// <summary>
		/// Sets the position of the underlying stream by reference to the limits of the chunk.
		/// </summary>
		/// <param name="offset">Number of bytes (positive or negative) by which to adjust the indicated origin.</param>
		/// <param name="origin">Use the beginning, current position or end of the chunk as the origin of the new position.</param>
		/// <returns></returns>
		public override long Seek( long offset, SeekOrigin origin )
		{
			switch( origin )
			{
				case SeekOrigin.Begin:
					if( offset < 0 || offset >= chunkLength )
						throw new ArgumentOutOfRangeException( String.Format( "Seek {0} from beginning is outside chunk boundary [{1}]", offset, Length ) );
					return underlyingStream.Seek( underlyingOffset + offset, origin ) - underlyingOffset;

				case SeekOrigin.Current:
					long posn = Position;
					if( posn + offset < 0 || posn + offset >= Length )
						throw new ArgumentOutOfRangeException( String.Format( "Seek {0} from current is outside chunk boundary [{1}]", offset, Length ) );
					return underlyingStream.Seek( offset, origin ) - underlyingOffset;

				case SeekOrigin.End:
					//NOTE: This uses a "Begin" offset on the underlying stream, with an offset calculated from the
					//		"offset from the end of the chunk" passed in.
					//
					if( chunkLength - 1 + offset < 0 || chunkLength - 1 + offset >= chunkLength )
						throw new ArgumentOutOfRangeException( String.Format( "Seek {0} from end is outside chunk boundary [{1}]", offset, Length ) );
					return underlyingStream.Seek( underlyingOffset + chunkLength - 1 + offset, SeekOrigin.Begin ) - underlyingOffset;

				default:
					throw new ArgumentException();
			}
		}

		public override void Flush()
		{
			throw new NotImplementedException();
		}

		public override void SetLength( long value )
		{
			throw new NotImplementedException();
		}
	}
}
