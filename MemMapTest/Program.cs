#define _USE_DISPOSE_

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace MemMapTest
{
	class Extent
	{
		private List<Extent> children = null ;

		public Extent FindExtentContaining( long offset )
		{
			foreach( Extent extent in children )
			{
				if( extent.Contains( offset ) )
					return extent ;
			}
			throw new ArgumentException( "Cannot find offset within extent-list" ) ;
		}

		public Extent CreateChildExtent( long offset, long length )
		{
			Extent existing = FindExtentContaining( offset ) ;

			if( existing.Status != Extent.ExtentStatus.Unassigned )
				throw new ArgumentOutOfRangeException( "offset", offset, "Already assigned to a partition" );
			if( !existing.Contains( offset + length ) )
				throw new ArgumentOutOfRangeException( "length", length, "Extends beyond end of containing extent" ) ;

			if( existing.Matches( offset, length ) )
			{
				existing.Status = Extent.ExtentStatus.Assigned ;
				return existing ;
			}
			if( existing.StartsAt( offset ) )
			{

			}
			//TODO
			return null;
		}
	}
		public enum ExtentStatus
		{
			Unassigned,
			Tentative,
			Assigned,
		}

		#region Public properties 
		public long			Length	{ get ; private set ; }
		public long			Offset	{ get ; private set ; }
		public ExtentStatus	Status	{ get ; private set ; }
		public Extent		Parent	{ get ; private set ; }
		#endregion
		#region Private members 
		private ExtentList	children;
		#endregion

		#region Constructors 
		public Extent( long offset, long length )
			: this( offset, length, ExtentStatus.Assigned, null )
		{
		}
		public Extent( long offset, long length, ExtentStatus status )
			: this( offset, length, status, null )
		{
		}
		public Extent( long offset, long length, ExtentStatus status, Extent parent )
		{
			Offset = offset ;
			Length = length ;
			Status = status ;
			Parent = parent ;
			children = null ;
		}
		#endregion

		#region Utility functions: inner/outer conversion; contains-tests 
		/// <summary>
		/// 
		/// </summary>
		public long OffsetBeyond
		{
			get { return Offset + Length ; }
		}
		public bool Contains( long outerOffset )
		{
			return ( Offset <= outerOffset ) && ( outerOffset < OffsetBeyond ) ;
		}
		public bool IsValid( long innerOffset )
		{
			return ( 0 <= innerOffset ) && ( innerOffset < Length ) ;
		}
		public bool Matches( long offset, long length )
		{
			return this.Offset == offset
				&& this.Length == length ;
		}
		public bool StartsAt( long offset )
		{
			return this.Offset == offset ;
		}
		public long InnerOffset( long outerOffset )
		{
			if( !Contains( outerOffset ) )
				throw new ArgumentOutOfRangeException( "outerOffset", outerOffset, "Not within extent " + this ) ;
			return outerOffset - Offset ;
		}
		public long OuterOffset( long innerOffset )
		{
			if( !IsValid( innerOffset ) )
				throw new ArgumentOutOfRangeException( "innerOffset", innerOffset, "Not within extent " + this ) ;
			return Offset + innerOffset ;
		}
		#endregion

		#region Child Extent handling 
		public Extent CreateChildExtent( long offset, long length )
		{
			if( children == null )
				children = new ExtentList( this ) ;
			return children.CreateChildExtent( offset, length ) ;
		}
		public Extent TruncateBefore( long offset )
		{
			Extent remainder = null ;
			return remainder ;
		}
		public void Unassign()
		{
		}
		#endregion
		public override string ToString()
		{
			return String.Format( "[ofs={0},len={1}]", Offset, Length ) ;
		}
	}

	class StreamExtent : Stream
	{
		private Extent	extent ;
		private Stream	stream;

		#region Dispose() framework 
#if _USE_DISPOSE_
		//----------------------------------------------------------------------------------------------------
		//	This 'how-to-handle-IDisposable' pattern comes courtesy of Reed Copsey at:
		//		http://reedcopsey.com/2009/03/20/idisposable-the-oft-misunderstood-and-misused-interface/
		//----------------------------------------------------------------------------------------------------

		/// <summary>
		/// Tracks whether object has been disposed.
		/// </summary>
		private bool disposed ;

		/*	The destructor (Finalizer in .NET-speak) should ONLY be declared if there are unmanaged (non-.NET)
		 *	resources owned by the class (because it makes things more complicated for the Garbage Collector
		 *	if an object has one).
		 *
		/// <summary>
		/// Finalizer: called prior to Garbage Collection.
		/// Calls Dispose() to only release unmanaged (non-.NET) resources.
		/// </summary>
		~StreamExtent()
		{
			this.Dispose( false ) ;
		}
		*/

		/*	This would be needed if deriving directly from 'IDispoable', but 'Stream' seems to follow
		 *	the same pattern as we're using, so 'public void Dispose()' already exists.
		 *
		/// <summary>
		/// Called to release/reset/free unmanaged (non-.NET) resources
		/// </summary>
		public void Dispose()
		{
			this.Dispose( true ) ;
			GC.SuppressFinalize( this ) ;
		}
		*/

		/*	If deriving directly from 'IDisposable', then the 'override' would need to be 'virtual'
		 *	to define the 'Dispose(bool)' pattern (and allow subclasses to override it).  Since
		 *	we're deriving from 'Stream', this has already been done, so we override and call the
		 *	base class (which would be omitted in the first case).
		 */
		/// <summary>
		/// Called by both Finalizer, ~StreamExtent(), and from Dispose() to free/release etc.
		/// unmanaged (non-.NET) resources and, optionally, managed resources.
		/// </summary>
		/// <param name="disposing"></param>
		protected override void Dispose( bool disposing )
		{
			if( !this.disposed )
			{
				if( disposing )		// of managed (.NET) resources
				{
					// If there are any managed, IDisposable resources, call Dispose() on them here.
					//...
					//TODO:
					//	May want an option when disposing of a top-level StreamExtent to also dispose
					//	the underlying stream.  This would most likely be the case if we were to add
					//	a constructor that took a filename instead of an existing Stream, and may be
					//	an option for existing constructors.  In either case, here is where we'd make
					//	the call.
				}
				// Dispose of any unmanaged (non-.NET) reosurces here.
				//...

				// Mark as disposed (to prevent above happening again, and to throw if other members called.
				this.disposed = true ;

				// Call the base class's Dispose() passing in the same flag.
				base.Dispose( disposing ) ;
			}
		}

		/// <summary>
		/// Ordinary member functions should call this to throw if they are called after disposal.
		/// </summary>
		protected void ThrowIfDisposed()
		{
			if( this.disposed )
				throw new ObjectDisposedException( "StreamExtent" ) ;
		}
#else
		/// <summary>
		/// Dummy function if not using Dispose()
		/// </summary>
		protected void ThrowIfDisposed()
		{
		}
#endif
		//----------------------------------------------------------------------------------------------------
		#endregion

		#region Constructors 
		//----------------------------------------------------------------------------------------------------
		/// <summary>
		/// Construct a StreamExtent over the whole of an existing Stream.
		/// </summary>
		/// <param name="stream">Underlying Stream object.</param>
		public StreamExtent( Stream stream )
			: this( stream, 0, stream.Length )
		{
		}
		/// <summary>
		/// Construct a StreamExtent of the given length starting at the current position of an existing Stream.
		/// </summary>
		/// <param name="stream">Underlying Stream object.</param>
		/// <param name="length">Length (in bytes) of the extent.</param>
		public StreamExtent( Stream stream, long length )
			: this( stream, stream.Position, length )
		{
		}
		/// <summary>
		/// Construct a StreamExtent over the specified region of an existing Stream.
		/// </summary>
		/// <param name="stream">Underlying Stream object.</param>
		/// <param name="offset">Start position of the extent.</param>
		/// <param name="length">Length (in bytes) of the extent.</param>
		public StreamExtent( Stream stream, long offset, long length )
		{
			if( !stream.CanSeek )
				throw new NotSupportedException( "Underlynig stream must be seekable" ) ;
			if( offset < 0 || offset > stream.Length )
				throw new ArgumentOutOfRangeException( "offset", offset, String.Format( "Negative or beyond end of stream ({0})", stream.Length ) ) ;
			if( length < 0 || offset+length > stream.Length )
				throw new ArgumentOutOfRangeException( "length", length, String.Format( "Negative or extends beyond end of stream ({0})", stream.Length ) ) ;
			this.stream = stream ;
			this.extent = new Extent( offset, length ) ;
			Position = 0 ;
		}
		//----------------------------------------------------------------------------------------------------
		#endregion

		#region Reflected properties/methods -- Reflect directly to underlying Stream 
		//----------------------------------------------------------------------------------------------------
		//	Overrides from 'Stream'
		//TODO:
		//	Should get/set call ThrowIfDisposed() ?
		//
		public override bool CanRead
		{
			get { return stream.CanRead ; }
		}
		public override bool CanSeek
		{
			get { return stream.CanSeek ; }
		}
		public override bool CanTimeout
		{
			get { return stream.CanTimeout ; }
		}
		public override bool CanWrite
		{
			get { return stream.CanWrite ; }
		}
		public override long Length
		{
			get { return extent.Length ; }
		}
		public override void Flush()
		{
			stream.Flush() ;
		}
		//----------------------------------------------------------------------------------------------------
		#endregion

		#region Position/Seek -- Reflect (with offset-tweaks) to underlying Stream 
		//----------------------------------------------------------------------------------------------------
		public override long Position
		{
			get
			{
				return extent.InnerOffset( stream.Position ) ;
			}
			set
			{
				Seek( value, SeekOrigin.Begin ) ;
			}
		}

		public override long Seek( long offset, SeekOrigin origin )
		{
			ThrowIfDisposed() ;

			switch( origin )
			{
				case SeekOrigin.Begin:
					return extent.InnerOffset( stream.Seek( extent.OuterOffset( offset ), SeekOrigin.Begin ) ) ;

				case SeekOrigin.Current:
					return Seek( Position + offset, SeekOrigin.Begin ) ;

				case SeekOrigin.End:
					return Seek( extent.Length + offset, SeekOrigin.Begin ) ;

				default:
					throw new ArgumentException();
			}
		}
		//----------------------------------------------------------------------------------------------------
		#endregion

		#region Read/Write -- Reflect (after bound-checking) to underlying Stream 
		//----------------------------------------------------------------------------------------------------
		public override int Read( byte[] buffer, int offset, int count )
		{
			ThrowIfDisposed() ;
			ThrowIfNoSpace( count ) ;
			return stream.Read( buffer, offset, count ) ;
		}
		public override int ReadByte()
		{
			ThrowIfDisposed() ;
			ThrowIfNoSpace( 1 ) ;
			return stream.ReadByte() ;
		}
		public override void Write( byte[] buffer, int offset, int count )
		{
			ThrowIfDisposed() ;
			ThrowIfNoSpace( count ) ;
			stream.Write( buffer, offset, count ) ;
		}
		public override void WriteByte( byte value )
		{
			ThrowIfDisposed() ;
			ThrowIfNoSpace( 1 ) ;
			stream.WriteByte( value );
		}
		//----------------------------------------------------------------------------------------------------
		#endregion

		#region Not Implemented 
		//----------------------------------------------------------------------------------------------------
		//	Not implemented (won't be!)
		//
		public override void SetLength( long value )
		{
			throw new NotImplementedException();
		}
		//----------------------------------------------------------------------------------------------------
		#endregion

		#region Helper functions 
		//----------------------------------------------------------------------------------------------------
		//	Helper functions
		//
		private void ThrowIfNoSpace( long count )
		{
			if( !extent.IsValid( Position + count ) )
				throw new ArgumentOutOfRangeException( "count", count, "Extends beyond end of extent " + extent ) ;
		}
		//----------------------------------------------------------------------------------------------------
		#endregion

		//TODO:
		//	Allow option to remove a child StreamExtent from its parent (setting the space back to "unassigned")?
	}

	class Program
	{
		static void Report( Stream stream, String msg )
		{
			Console.WriteLine( "Stream: {0} [{1}]", msg, stream ) ;
			try {
				Console.WriteLine( "  Posn: {0}", stream.Position ) ;
				Console.WriteLine( "  Size: {0}", stream.Length ) ;
			} catch( ObjectDisposedException ) {
				Console.WriteLine( "  -- disposed --" ) ;
			} catch( Exception ex ) {
				Console.WriteLine( "  -- {0} --", ex.Message ) ;
			}
		}
		static void Report( BinaryReader reader, String msg )
		{
			Console.WriteLine( "BinaryReader: {0} [{1}]", msg, reader ) ;
			try {
				Console.WriteLine( "  Posn: {0}", reader.BaseStream.Position ) ;
				Console.WriteLine( "  Size: {0}", reader.BaseStream.Length ) ;
			} catch( ObjectDisposedException ) {
				Console.WriteLine( "  -- disposed" ) ;
			} catch( Exception ex ) {
				Console.WriteLine( "  -- {0}", ex.Message ) ;
			}
		}
		static void Report( StreamReader reader, String msg )
		{
			Console.WriteLine( "StreamReader: {0} [{1}]", msg, reader ) ;
			try {
				Console.WriteLine( "  Posn: {0}", reader.BaseStream.Position ) ;
				Console.WriteLine( "  Size: {0}", reader.BaseStream.Length ) ;
			} catch( ObjectDisposedException ) {
				Console.WriteLine( "  -- disposed" ) ;
			} catch( Exception ex ) {
				Console.WriteLine( "  -- {0}", ex.Message ) ;
			}
		}
		static void Main( string[] args )
		{
			Stream stream = File.Open( @"C:\BI2EDD.DBG", FileMode.Open, FileAccess.Read ) ;
			Report( stream, "Open()" ) ;

			BinaryReader br = new BinaryReader( stream ) ;
			Report( br, "new()" ) ;
			Report( stream, "new()" ) ;

			StreamReader sr = new StreamReader( stream ) ;

			br = null ;
			Report( stream, "br null-ed" ) ;
			sr = null ;
			Report( stream, "sr null-ed" ) ;

			if( sr != null )
			{
				sr.Close() ;
				Report( sr, "Close()" ) ;
			}

			Report( stream, "Close()" ) ;
			Console.ReadKey() ;
		}
	}
}
