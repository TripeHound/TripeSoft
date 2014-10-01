//#define DBG
//#define NEWFILE
using System ;
using System.Collections.Generic ;
using System.Text ;
using System.IO ;
using System.Runtime.InteropServices ;
using System.Net ;
using System.Windows.Forms ;
using TripeSoft.Stream;
using System.Xml;
using Tripe.Utils ;

//===================================================================================================
//	MOBI/EXTH Formatting taken from:		http://wiki.mobileread.com/wiki/MOBI
//	Underlying Palm Format from:			http://wiki.mobileread.com/wiki/PDB
//
//	Thanks and recognition to all concerned in compiling the above!
//===================================================================================================

//===================================================================================================
//	WEIRD THINGS / KNOWN ISSUES / ACCOMMODATIONS
//---------------------------------------------------------------------------------------------------
//	GENERAL
//
//	A MOBI file (including Amazon AZW files) is based on the Palm Database format.  In essence, this
//	is a header ('PalmFileHeader') followed by a list of 'PalmRecordInfo' entries that locate the
//	database records.  The remainder of the file are those records.
//
//	For our [current] purposes, we are only interested in the first PDB record ('PDB Record 0') which
//	contains the meta-data about the book (Author, Title, and sundry other info).  The remaining PDB
//	records contain the [compressed/encrypted] book contents, cover images, and other bits and pieces.
//
//	 Palm File Layout							 Palm Record Zero
//	+---------------------------+				+---------------------------------+
//	| PalmFileHeader   78 bytes |				| PalmDocHeader          16 bytes |
//	| - - - - - - - - - - - - - |				| - - - - - - - - - - - - - - - - |
//	| PalmRecordInfo   variable |				| MOBIHeader       228..256 bytes |
//	+---------------------------+				| inc. drmOffset                  |
//	|                           |				|      fullNameOffset             |
//	| Palm Record 0             |				| - - - - - - - - - - - - - - - - |
//	|                           |				| EXTH Block             variable |
//	+---------------------------+				| inc. padding?                   |
//	|                           |				+---------------------------------+
//	.                           .				.           ? gap/data ?          .
//	. .. other Palm records ..  .				+---------------------------------+
//	.                           .				| DRMData       optional,variable |
//	|                           |				+---------------------------------+
//	+---------------------------+				.           ? gap/data ?          .
//	|                           |				+---------------------------------+
//	| Palm Record (n-1)         |				| FullName               variable |
//	|                           |				+---------------------------------+
//	+---------------------------+				.           ? gap/data ?          .
//												+---------------------------------+
//
//	The layout of 'PDB Record 0' is not formally defined, but much of it has been deduced (by others,
//	see above).  The "derived spec" leaves some areas of ambiguity and some flexibility in how the
//	record is laid out:  One of the problems/decisions when writing this MOBI-mangling library/utility,
//	is whether I try to support "everything that COULD be done, in theory", or "just the variants [I'm]
//	likely to see".
//
//	Known:				o	The size of the whole record can be determined from the PDB structure.
//						o	The PalmDocHeader is always the first 16 bytes.
//						o	The MOBI Header follows immediately. Its size can vary, but is included
//							within itself.  The header includes 'drmOffset' and 'fullNameOffset'
//							fields that point to datablocks within PDBRecordZero (the first is optional).
//						o	The EXTH Block immediately follows the MOBI Header.  This is where most of
//							the meta-data is held.  Its length is variable and held in the EXTH header.
//							It includes/is-followed-by zero-byte padding to get to a four-byte boundary.
//						o	The 'FullName' and 'DRMData' (if used) have their length(s) specified in
//							the MOBI header.
//
//	Observed Variants:	o	MOBI Header sizes of 228, 232, 248 and 256 have been seen.
//						o	The EXTH Block is zero-byte-padded to a multiple of four.  Some files
//							include these padding bytes in the EXTH's 'headerLength' field; some don't.
//							(I call these cases INTERNAL and EXTERNAL respectively).  Some files (using
//							external padding) ALWAYS add padding bytes (that is, they have 1..4 bytes
//							instead of 0..3 bytes).  Amazon-generated files use 0..3 bytes of internal
//							padding; Calibre-gnerated files use 1..4 of external padding.
//						o	In the only source of DRMed files seen (Amazon) the DRM Data immediately
//							follows the [padded] EXTH Block.
//						o	In all the files I've seen (110+ Amazon; 300+ Calibre-generated) the
//							FullName block immediately follows the [padded] EXTH block (or the DRM
//							block, if present).
//						o	All the files seen have "spare space" at the end of the PDB record. For
//							Amazon files, this is generally in the range 74xx..79xx bytes; Calibre
//							files seem to have either 8188 or 8192 bytes.  In all cases, this spare
//							space is zero-filled.
//
//	Unseen Variants:	o	In theory the order of the DRMData and the FullName blocks could be swapped,
//							and they could appear at any position within the PDB record (i.e. there
//							could be gaps and/or extra data as shown in the diagram above).
//
//	This isn't JUST a question of how much effort to put in (honest!).  If the code "tries" to
//	accommodate everything, including variants I haven't seen, then if/when previously unseen variants
//	are encountered it MAY work, but could break something because it doesn't REALLY know what to do
//	with the extra stuff.  Conversely, if it only processes the variants I've seen, and complains about
//	anything unexpected, then at least you are aware that something cannot be handled.
//
//	TODO:	Decide how flexible to be: the ideal would be to have lots of flags that modify behaviour:
//			o	Pad EXTH internall, externally, as-the-source-was.
//			o	Strip/preserve unneccessary padding.
//			o	Keep/drop/complain if there are gaps between EXTH/DRM/FullName
//			o	Preserve/swap/complain if DRM/FullName are swapped.
//
//---------------------------------------------------------------------------------------------------
//	EXTH Padding
//
//	The 'spec' on MobiRead says that the EXTH block is followed by zero-byte padding to bring things
//	up to a four-byte boundary.  It further says:
//
//		o	Any padding is NOT included in the EXTH's 'headerLength' field.
//		o	No padding is added if already at a four-byte boundary.
//
//	However, observation of existing files reveals:
//
//		o	All (my) Amazon-delivered .AZWs (including converted Personal Documents) INCLUDE the
//			count of any additional padding in 'headerLength' (i.e., 'headerLength' will always
//			be a multiple of 4).  Padding is not added if it is not needed.
//		o	All of my Calibre-generated .MOBIs exclude padding bytes from 'headerLength' (i.e. it
//			may not be a multiple of 4, but the EXTH block as a whole is).  In all [seen] cases,
//			if no padding WOULD be needed (i.e. already on a 4-byte boundary), then FOUR bytes are
//			used.
//
//	Thus both differ from the 'spec': Amazon 'correctly' use 0..3 bytes of padding, but make it INTERNAL
//	to the EXTH block; Calibre uses 1..4 bytes but 'correctly' has them EXTERNAL to the EXTH block.
//
//	For reference: the Perl 'mobi2mobi' utility expects INTERNAL padding -- i.e. 'headerLength' should
//	be a multiple of four: Amazon AZWs are OK; Calibre files with 1..3 bytes of padding are flagged as
//	incorrect (files with 4 bytes of [unneeded] padding are OK because the headerLength is zero, MOD 4).
//
//	If mobi2mobi modifies the EXTH block, internal padding is ADDED if needed; any original external
//	padding (1..4 bytes) seems to be treated as "extra content" between the EXTH block and the FullName
//	and gets preserved (see EXTRA CONTENT for more discussion on this).
//	
//---------------------------------------------------------------------------------------------------
//	Side-files (from http://www.mobileread.com/forums/showthread.php?t=194234)
//
//		.ea/.eal:								End Actions (The 'Before you go..' at the end of a book).
//		.mpb/.mpb1/.tan/.tas/.tal/.mbs/.han:	Notes, Bookmarks & Progress
//		.phl:									Popular Highlights
//		.azw:									Mobi book
//		.tpz/.azw1:								Topaz book
//		.pobi:									Periodical
//		.azw2:									Kindlet (Active content)
//		.azw3:									KF8 book
//		.azw4:									Print-Replica book
//		.apnx:									Page Numbers
//
//		There's also a bunch of other misc stuff in the sidecar (.sdr) folder on the K5: .azw3f, .azw3r,
//		and .asc for X-Ray (among other things).
//---------------------------------------------------------------------------------------------------
//===================================================================================================

namespace Tripe.Utils {
#if __reporting__not__ready__
	class Reporting {
		public enum Level {
			Ignore,
			Note,
			Warning,
			Error,
			Fatal
		}

		public StringBuilder	Messages	{ get ;				private set ;				}
		public StringBuilder	Log			{ get ;				private set ;				}

		public int				NoteCount	{ get ;				private set ;				}
		public int				WarningCount{ get ;				private set ;				}
		public int				ErrorCount	{ get ;				private set ;				}
		public int				FatalCount	{ get ;				private set ;				}

		public Level			IgnoreLevel	{ get ;				set ;						}
		public Level			FatalLevel	{ get ;				set ;						}

		public void Oops( Level level, string msg ) {
			if( level <= IgnoreLevel )
				return ;
			Messages.Append( level.ToString() + ": " + msg + "\r\n" ) ;
		}

	}
#endif
	class Trace {
#if DBG
		public static bool ConsoleOut = true ;
#else
		public static bool ConsoleOut = false ;
#endif

		static Trace() {
			string traceDbg = Environment.GetEnvironmentVariable( "TripeDbg" ) ;
			if( traceDbg != null ) {
				if( traceDbg.CompareTo( "0" ) == 0 )
					Utils.Trace.ConsoleOut = false ;
				else if( traceDbg.CompareTo( "1" ) == 0 )
					Utils.Trace.ConsoleOut = true ;
			}
		}
		public static void Msg( string msg ) {
			if( ConsoleOut ) Console.Write( msg ) ;
		}
		public static void Msg( string msg, Object obj0 ) {
			if( ConsoleOut ) Console.Write( msg, obj0 ) ;
		}
		public static void Msg( string msg, Object obj0, Object obj1 ) {
			if( ConsoleOut ) Console.Write( msg, obj0, obj1 ) ;
		}
		public static void Msg( string msg, Object obj0, Object obj1, Object obj2 ) {
			if( ConsoleOut ) Console.Write( msg, obj0, obj1, obj2 ) ;
		}
		public static void Msg( string msg, Object obj0, Object obj1, Object obj2, Object obj3 ) {
			if( ConsoleOut ) Console.Write( msg, obj0, obj1, obj2, obj3 ) ;
		}

		public static void MsgLine() {
			if( ConsoleOut ) Console.WriteLine() ;
		}
		public static void MsgLine( string msg ) {
			if( ConsoleOut ) Console.WriteLine( msg ) ;
		}
		public static void MsgLine( string msg, Object obj0 ) {
			if( ConsoleOut ) Console.WriteLine( msg, obj0 ) ;
		}
		public static void MsgLine( string msg, Object obj0, Object obj1 ) {
			if( ConsoleOut ) Console.WriteLine( msg, obj0, obj1 ) ;
		}
		public static void MsgLine( string msg, Object obj0, Object obj1, Object obj2 ) {
			if( ConsoleOut ) Console.WriteLine( msg, obj0, obj1, obj2 ) ;
		}
		public static void MsgLine( string msg, Object obj0, Object obj1, Object obj2, Object obj3 ) {
			if( ConsoleOut ) Console.WriteLine( msg, obj0, obj1, obj2, obj3 ) ;
		}
	}
}
namespace Tripe.eBook {
	/// <summary>
	/// Exception thrown when not at expected position processing a MOBI header.
	/// </summary>
	class MobiPositionException : Exception {
		/// <summary>
		/// Exception thrown when not at expected position processing a MOBI header.
		/// </summary>
		/// <param name="reason">What was unexpected.</param>
		/// <param name="filePosn">Position in underlying PDB file.</param>
		/// <param name="mobiPosn">File-position of MOBI header.</param>
		/// <param name="itemOffset">Offset within MOBI header.</param>
		public MobiPositionException( string reason, UInt32 filePosn, UInt32 mobiPosn, UInt32 itemOffset ) :
			base( string.Format( "Mobi Position Exception: {0}\r\n"
								+"   Underlying file position: {1:x8}, {1}\r\n"
								+"     Address of MOBI header: {2:x8}, {2}\r\n"
								+"  Offset within MOBI header: {3:x8}, {3}\r\n"
								+"     Expected file position: {4:x8}, {4}\r\n"
								+"                 Difference: {5:x8}, {5}\r\n",
								reason,
								filePosn,
								mobiPosn,
								itemOffset,
								mobiPosn+itemOffset,
								(int)(mobiPosn+itemOffset - filePosn) ) )
		{
		}
	}

	/// <summary>
	/// Intended to encapsulate Palm Database (PDB) files, at least in so far as is necessary to
	/// support handling MOBI-format files.
	/// <para/>
	/// TODO: Some of the "utility" functions (DumpBytes(); GetSwapped32(); etc.) might be better 
	/// in a utility class.
	/// <para/>
	/// TODO: Framework to handle reading/editing/creating/deleting records.  Because all the
	/// "interesting" MOBI stuff is in the first record, the initial implementation will probably
	/// only handle re-writing that record, and then adjusting the record offsets for remaining
	/// records.
	/// </summary>
	public class PalmFile {
		//===============================================================================================
		//	_NonRef32()
		//	NonRef32()
		//	Encapsulation of a UInt32 to provide an explicit "non ref" mechanism.
		//
		//	Problem:	You have a function that uses a "ref" modifier so it can update one of its
		//				parameters.  You want to provide a non-ref version, so it can also be called
		//				with a fixed value (so you don't need to define a redundant variable). Doing:
		//					void MyFunction( ref UInt32 myParam ) {...}
		//					void MyFunction(     UInt32 myParam ) {...}
		//				will allow:
		//					MyFunction( ref variable_that_gets_changed ) ;
		//					MyFunction( 56 ) ;
		//				but is not ideal because it also lets through:
		//					MyFunction( variable_that_wont_change_because_I_forgot_the_REF ) ;
		//
		//	Answer:		Using _NonRef32 as a parameter type, you define:
		//					void MyFunction( ref UInt32 myParam ) {...}
		//					void MyFunction(  _NonRef32 myParam ) {...}
		//				which allows:
		//					MyFunction( ref variable_that_gets_changed ) ;
		//					MyFunction( NonRef32( 56 ) ) ;
		//					MyFunction( NonRef32( variable_I_dont_want_changing ) ) ;
		//				and throws an error for:
		//					MyFunction( variable_that_wont_change_because_I_forgot_the_REF ) ;
		//-----------------------------------------------------------------------------------------------
		protected class _NonRef32 {
			private UInt32 value ;
			public _NonRef32 ( UInt32 value )						{ this.value = value ;				}
			public static implicit operator UInt32( _NonRef32 that ){ return that.value ;				}
		}
		protected static _NonRef32 NonRef32( UInt32 value )			{ return new _NonRef32( value ) ;	}

		//===============================================================================================
		//	DumpBytes()
		//	Routines for printing blocks of byte-data to the console.
		//-----------------------------------------------------------------------------------------------

		/// <summary>
		/// Dump an array of bytes including their original file-position and offset.
		/// </summary>
		/// <param name="data">Array of bytes to be displayed.</param>
		/// <param name="title">Description of the array.</param>
		/// <param name="filePosn">Base file-position from where the bytes came.</param>
		/// <param name="offset">Offset (relative to file-position) of bytes.</param>
		protected static void DumpBytes( byte[] data, string title, UInt32 filePosn, UInt32 offset ) {
			DumpBytes( data, string.Format( "{0} @ [{1:x8}, {1}] = [{2:x8}, {2}] + [{3:x8}, {3}]", title, filePosn + offset, filePosn, offset ) ) ;
		}

		/// <summary>
		/// Dump an array of bytes including their original file-position.
		/// </summary>
		/// <param name="data">Array of bytes to be displayed.</param>
		/// <param name="title">Description of the array.</param>
		/// <param name="filePosn">File-position from where the bytes came.</param>
		protected static void DumpBytes( byte[] data, string title, UInt32 filePosn ) {
			DumpBytes( data, string.Format( "{0} @ [{1:x8}, {1}]", title, filePosn ) ) ;
		}
		/// <summary>
		/// Dump an array of bytes.
		/// </summary>
		/// <param name="data">Array of bytes to be displayed.</param>
		/// <param name="title">Description of the array.</param>
		protected static void DumpBytes( byte[] data, string title ) {
			int i, j ;
			Trace.MsgLine( "Dump {0} byte(s): {1}", data.Length, title ) ;
			for( i=0 ; i < data.Length ; i++ )
				if( data[i] != 0 )
					break ;
			if( i == data.Length ) {
				Trace.MsgLine( "000000  00 * {0}\r\n", data.Length ) ;
				return ;
			}

			for( i=0 ; i < data.Length ; i += 16 ) {
				Trace.Msg( "{0:x6} ", i ) ;
				for( j=0 ; (j < 16) && (i+j < data.Length) ; j++ ) {
					if( j == 8 ) Trace.Msg( " -" ) ;
					Trace.Msg( " {0:x2}", data[i+j] ) ;
				}
				for( /**/ ; j < 16 ; j++ ) {
					if( j == 8 ) Trace.Msg( " -" ) ;
					Trace.Msg( "   " ) ;
				}
				Trace.Msg( "   " ) ;
				for( j=0 ; (j < 16) && (i+j < data.Length) ; j++ )
					Trace.Msg( "{0}", (data[i+j] < 32 ? '.' : (char)data[i+j]) ) ;
				Trace.MsgLine() ;
			}
			Trace.MsgLine() ;
		}

		//===============================================================================================
		//	Swap..()
		//	Routines for swapping byte-order of various length unsigned integers.
		//-----------------------------------------------------------------------------------------------
		/// <summary>
		/// Swap the bytes of a 16-bit unsigned int.
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		private static UInt16 Swap( UInt16 data ) {
			return (UInt16) IPAddress.NetworkToHostOrder( (short)data ) ;
		}

		/// <summary>
		/// Swap the bytes of a 32-bit unsigned int.
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		private static UInt32 Swap( UInt32 data ) {
			return (UInt32) IPAddress.NetworkToHostOrder( (int)data ) ;
		}
		/// <summary>
		/// Swap the bytes of a 64-bit unsigned int.
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		private static UInt64 Swap( UInt64 data ) {
			return (UInt64) IPAddress.NetworkToHostOrder( (long)data ) ;
		}

		//===============================================================================================
		//	GetSwapped..()
		//	Get various length unsigned integers from a "byte-stream" and swap their constituent bytes.
		//	Update the position within the parent stream.
		//-----------------------------------------------------------------------------------------------
		protected static UInt16 GetSwapped16( byte[] fileData, ref UInt32 filePosn ) {
			UInt16 result = Swap( BitConverter.ToUInt16( fileData, (int)filePosn ) ) ;
			filePosn += sizeof(UInt16) ;
			return result ;
		}
		protected static UInt32 GetSwapped32( byte[] fileData, ref UInt32 filePosn ) {
			UInt32 result = Swap( BitConverter.ToUInt32( fileData, (int)filePosn ) ) ;
			filePosn += sizeof(UInt32) ;
			return result ;
		}
		protected static UInt64 GetSwapped64( byte[] fileData, ref UInt32 filePosn ) {
			UInt64 result = Swap( BitConverter.ToUInt64( fileData, (int)filePosn ) ) ;
			filePosn += sizeof(UInt64) ;
			return result ;
		}

		//===============================================================================================
		//	PutSwapped..()
		//	Send various length unsigned integers to a file after swapping their constituent bytes.
		//-----------------------------------------------------------------------------------------------
		protected static UInt32 PutSwapped16( BinaryWriter bw, UInt16 data ) {
			bw.Write( Swap( data ) ) ;
			return sizeof(UInt16) ;
		}
		protected static UInt32 PutSwapped32( BinaryWriter bw, UInt32 data ) {
			bw.Write( Swap( data ) ) ;
			return sizeof(UInt32) ;
		}
		protected static UInt32 PutSwapped64( BinaryWriter bw, UInt64 data ) {
			bw.Write( Swap( data ) ) ;
			return sizeof(UInt64) ;
		}

		//===============================================================================================
		//	GetBytes()
		//	Create a byte[] from a given number of bytes in a "byte-stream", optionally updating the
		//	position within the parent stream.
		//-----------------------------------------------------------------------------------------------
		protected static byte[] GetBytes( byte[] fileData, _NonRef32 filePosn, UInt32 count ) {
			UInt32 copy = filePosn ;
			return GetBytes( fileData, ref copy, count ) ;
		}
		protected static byte[] GetBytes( byte[] fileData, ref UInt32 filePosn, UInt32 count ) {
			if( fileData.Length - filePosn < count ) throw new Exception( string.Format( "Not enough data: fileData.Length:{0}, filePosn:{1}, count:{2} ({3} available)", fileData.Length, filePosn, count, fileData.Length-filePosn ) ) ;
			byte[] result = new byte[count] ;
			for( int i=0 ; i < count ; i++ )
				result[i] = fileData[filePosn++] ;
			return result ;
		}

		//===============================================================================================
		//	PutBytes()
		//	Send the whole, or selected part, of a byte-array to a file.
		//-----------------------------------------------------------------------------------------------
		protected static UInt32 PutBytes( BinaryWriter bw, byte[] data ) {
			bw.Write( data ) ;
			return (UInt32)data.Length ;
		}
		protected static UInt32 PutBytes( BinaryWriter bw, byte[] data, UInt32 index, UInt32 count ) {
			bw.Write( data, (int) index, (int) count ) ;
			return count ;
		}

		//===============================================================================================
		//	SkipToFourByteBoundary()
		//	RoundToFourByteBoundary()
		//	PaddingNeededToFourByteBoundary()
		//	Routines to update a position to a four-byte boundary, or to return the padding needed to
		//	get there.
		//	TODO:	Could probably merge the first two (pos. using NonRef32).
		//-----------------------------------------------------------------------------------------------
		protected static UInt32 SkipToFourByteBoundary( ref UInt32 filePosn ) {
			UInt32 original = filePosn ;
			while( (filePosn % 4) != 0 ) filePosn++ ;
			return filePosn - original ;
		}
		protected static UInt32 RoundToFourByteBoundary( UInt32 value ) {
			return (value + 3) & ~3U ;
		}
		protected static UInt32 PaddingNeededToFourByteBoundary( UInt32 value ) {
			return 3 - (value + 3) & 3 ;
		}

		//===============================================================================================
		//	TODO:	Make this the base of MOBIHeader, EXTHheader etc. ... Have LoadFrom() do the setting
		//			of Address/Size before/after the data is read; calling an abstract method to do the
		//			real stuff?  ?? Instead of checking Size, have a MinSize (the least that can be read
		//			before checking further??
		//-----------------------------------------------------------------------------------------------
		protected abstract class MobiElement {
			public UInt32 Address {
				get ;
				protected set ;
			}
			public virtual UInt32 Length {
				get ;
				protected set ;
			}
			public virtual UInt32 MinSize {
				get { return 0 ; }
				protected set {}
			}
			protected abstract void LoadElementFrom( byte[] fileData, ref uint filePosn ) ;
			public void LoadFrom( byte[] fileData, ref uint filePosn ) {
				//Trace.MsgLine( "{0}.LoadFrom( {1:x8}, {1} )", this.GetType(), filePosn ) ;
				if( fileData == null ) throw new ArgumentException() ;
				if( fileData.Length - filePosn < MinSize ) throw new ArgumentException() ;

				Address = filePosn ;
				LoadElementFrom( fileData, ref filePosn ) ;
				Length = filePosn - Address ;
			}
		}
		protected class MobiDataBlock : MobiElement {
			protected byte[]	data ;

			public override UInt32 Length {
				get {
					return (UInt32)data.Length ;
				}
			}
			public override UInt32 MinSize {
				get ;
				protected set ;
			}
			public string Title {
				get ;
				private set ;
			}
			public MobiDataBlock() {}
			public MobiDataBlock( byte[] fileData, ref uint filePosn, UInt32 count, string title ) {
				MinSize = count ;
				Title = title ;
				LoadFrom( fileData, ref filePosn ) ;
			}
			protected override void LoadElementFrom( byte[] fileData, ref uint filePosn ) {
				data = GetBytes( fileData, ref filePosn, MinSize ) ;
				Length = (UInt32) data.Length ;
			}
			public UInt32 WriteTo( BinaryWriter bw ) {
				return PutBytes( bw, data ) ;
			}
			public void Dump() {
				DumpBytes( data, Title, Address ) ;
//				Trace.MsgLine( ToString() ) ;
			}
//			public override string ToString() {
//			}
			public void Replace( byte[] value ) {	//need to include
				data = value ;
				Length = (UInt32) data.Length ;
			}
			public static implicit operator byte[] ( MobiDataBlock value ) {
				return value.data ;
			}

			public bool AllZero() {
				foreach( byte b in data )
					if( b != 0x00 )
						return false ;
				return true ;
			}
		}

		//===============================================================================================
		//-----------------------------------------------------------------------------------------------
		protected class Mobi32 : IFormattable {
			protected UInt32 value ;
			public Mobi32() {}
			public Mobi32( UInt32 value )							{ this.value = value ;				}
			public static implicit operator UInt32( Mobi32 that )	{ return that.value ;				}
			public static implicit operator Mobi32( UInt32 that )	{ return new Mobi32( that ) ;		}
			public override string ToString()						{ return ToString( null, null ) ;	}
			public string ToString( string fmt )					{ return ToString( fmt, null ) ;	}
			public string ToString( IFormatProvider fp )			{ return ToString( null, fp ) ;		}
			public virtual string ToString( string fmt, IFormatProvider fp ) {
				return value.ToString( fmt, fp ) ;
			}
		}
		protected class MobiID : Mobi32 {
			public MobiID( UInt32 value )							{ this.value = value ;				}
			public static implicit operator UInt32( MobiID that )	{ return that.value ;				}
			public static implicit operator MobiID( UInt32 that )	{ return new MobiID( that ) ;		}
			//public override string ToString()						{ return ToString( null, null ) ;	}
			//public string ToString( string fmt )					{ return ToString( fmt, null ) ;	}
			//public string ToString( IFormatProvider fp )			{ return ToString( null, fp ) ;		}
			public override string ToString( string fmt, IFormatProvider fp ) {
				if( fmt == null || fmt == "" ) {
					return new string( new char[]{
						(char)(value >> 24 & 0xff),
						(char)(value >> 16 & 0xff),
						(char)(value >>  8 & 0xff),
						(char)(value       & 0xff),
					} ) ;
				}
				return base.ToString( fmt, fp ) ;
			}
		}
		protected class MobiTime : Mobi32 {
			public MobiTime( UInt32 value )							{ this.value = value ;				}
			public static implicit operator UInt32( MobiTime that )	{ return that.value ;				}
			public static implicit operator MobiTime( UInt32 that )	{ return new MobiTime( that ) ;		}
			//public override string ToString()						{ return ToString( null, null ) ;	}
			//public string ToString( string fmt )					{ return ToString( fmt, null ) ;	}
			//public string ToString( IFormatProvider fp )			{ return ToString( null, fp ) ;		}
			public override string ToString( string fmt, IFormatProvider fp ) {
				if( fmt == null || fmt == "" ) {
					DateTime dateBase = ( (value & 0x80000000) == 0 )
						? new DateTime( 1970, 1, 1 )
						: new DateTime( 1904, 1, 1 ) ;
					return (dateBase + new TimeSpan( value * 10000000L )).ToString() ;
				}
				return base.ToString( fmt, fp ) ;
			}
		}
/*		class MobiData : IFormattable {
		private byte[] value ;
		private UInt32 size ;
		public MobiData( byte[] value )							{ this.value = value ;			}
		public static implicit operator byte[]( MobiData that )	{ return that.value ;			}
		public static implicit operator MobiData( byte[] that )	{ return new MobiData( that ) ;	}
		public virtual string ToString( string fmt, IFormatProvider fp ) {
			if( fmt == null || fmt == "" ) {
			}
			return value.ToString( fmt, fp ) ;
		}
*/
		/// <summary>
		/// Represents the location within a PDB file of a database record.  A block of these follow
		/// the PalmFileHeader block.
		/// <para/>
		/// TODO: Probably could do with a built-in way of finding the length of these records: the
		/// per-record header does not include a length, so this can only be determined by where the
		/// following record starts.  However, in the general case, the "following record" may not be 
		/// the next entry in the PalmRecordInfo list.
		/// </summary>
		protected struct PalmRecordInfo {
			public UInt32	filePosn ;
			public UInt32	id_attr ;

			public UInt32	Size				{ get { return 8 ; } }		//Fixed
			public UInt32	Address ;

			public UInt32 AddressOf( UInt32 offset ) {
				return this.filePosn + offset ;
			}

			public PalmRecordInfo( byte[] fileData, ref uint filePosn ) : this() {
				LoadFrom( fileData, ref filePosn ) ;
			}
			public void LoadFrom( byte[] fileData, ref uint filePosn ) {
				//Trace.MsgLine( "{0}.LoadFrom( {1:x8}, {1} )", this.GetType(), filePosn ) ;
				if( fileData == null ) throw new ArgumentException() ;
				if( fileData.Length - filePosn < Size ) throw new ArgumentException() ;

				Address = filePosn ;
				this.filePosn	= GetSwapped32( fileData, ref filePosn ) ;
				this.id_attr	= GetSwapped32( fileData, ref filePosn ) ;
			}
			public UInt32 WriteTo( BinaryWriter bw, UInt32 adjustment ) {
				return	PutSwapped32( bw, filePosn + adjustment )
					+	PutSwapped32( bw, id_attr ) ;
			}
			public void Dump() {
				Trace.MsgLine( ToString() ) ;
			}
			public override string ToString() {
				return string.Format( "PalmRecordInfo [size={0}] at filePosn {1:x8}, {1}:\r\n", Size, Address ) +
					string.Format( "              offset: 0x{0:x8}, {0}\r\n",		filePosn			) +
					string.Format( " uniqueID/attributes: 0x{0:x8}, {0}\r\n",		id_attr			) ;
			}
		}

		/// <summary>
		/// Encapsulates the file-header of a Palm database (PDB) file.  The fixed header is followed
		/// by a number of PalmRecordInfo blocks, one for each record in the database.  The records
		/// themselves start after two-bytes of padding (to get a four-byte boundary).
		/// <para/>
		/// TODO: The 'appInfoID' and 'sortInfoID' can [apparently] point to data between the header
		/// and the first record, but probably not in MOBI files.
		/// <para/>
		/// TODO: More "generic PDB" handling methods (as opposed to "only what's needed to edit the
		/// meta-data of a MOBI file" methods).
		/// </summary>
		protected struct PalmFileHeader {
			public byte[]	databaseName ;
			public UInt16	attributes ;
			public UInt16	version ;
			public MobiTime	dateCreated ;
			public MobiTime	dateModified ;
			public MobiTime	dateBackedup ;
			public UInt32	modification ;
			public UInt32	appInfoID ;
			public UInt32	sortInfoID ;
			public MobiID	type ;
			public MobiID	creator ;
			public UInt32	uniqueIDseed ;
			public UInt32	nextRecordListID ;
			public UInt16	recordCount ;
			PalmRecordInfo[]records ;
			public UInt16	padding ;

			public UInt32	Size ;
			public UInt32	Address ;

			/// <summary>
			/// Create a PalfFileHeader by loading from the given position in the given data.
			/// </summary>
			/// <param name="fileData"></param>
			/// <param name="filePosn"></param>
			public PalmFileHeader( byte[] fileData, ref uint filePosn ) : this() {
				LoadFrom( fileData, ref filePosn ) ;
			}
			public void LoadFrom( byte[] fileData, ref uint filePosn ) {
				//Trace.MsgLine( "{0}.LoadFrom( {1:x8}, {1} )", this.GetType(), filePosn ) ;
				if( fileData == null ) throw new ArgumentException() ;
				if( fileData.Length - filePosn < 78 ) throw new ArgumentException() ;

				Address = filePosn ;
				databaseName	= GetBytes( fileData, ref filePosn, 32 ) ;
				attributes		= GetSwapped16( fileData, ref filePosn ) ;
				version			= GetSwapped16( fileData, ref filePosn ) ;
				dateCreated		= GetSwapped32( fileData, ref filePosn ) ;
				dateModified	= GetSwapped32( fileData, ref filePosn ) ;
				dateBackedup	= GetSwapped32( fileData, ref filePosn ) ;
				modification	= GetSwapped32( fileData, ref filePosn ) ;
				appInfoID		= GetSwapped32( fileData, ref filePosn ) ;
				sortInfoID		= GetSwapped32( fileData, ref filePosn ) ;
				type			= GetSwapped32( fileData, ref filePosn ) ;
				creator			= GetSwapped32( fileData, ref filePosn ) ;
				uniqueIDseed	= GetSwapped32( fileData, ref filePosn ) ;
				nextRecordListID= GetSwapped32( fileData, ref filePosn ) ;
				recordCount		= GetSwapped16( fileData, ref filePosn ) ;
			
				records = new PalmRecordInfo[recordCount] ;
				for( int i=0 ; i < recordCount ; i++ )
					records[i].LoadFrom( fileData, ref filePosn ) ;

				padding			= GetSwapped16( fileData, ref filePosn ) ;

				Size = filePosn - Address ;
			}
			public UInt32 WriteTo( BinaryWriter bw, UInt32 adjustment ) {
				UInt32 result = PutBytes( bw, databaseName )
							+	PutSwapped16( bw, attributes		)
							+	PutSwapped16( bw, version			)
							+	PutSwapped32( bw, dateCreated		)
							+	PutSwapped32( bw, dateModified		)
							+	PutSwapped32( bw, dateBackedup		)
							+	PutSwapped32( bw, modification		)
							+	PutSwapped32( bw, appInfoID			)
							+	PutSwapped32( bw, sortInfoID		)
							+	PutSwapped32( bw, type				)
							+	PutSwapped32( bw, creator			)
							+	PutSwapped32( bw, uniqueIDseed		)
							+	PutSwapped32( bw, nextRecordListID	)
							+	PutSwapped16( bw, recordCount		) ;
				result += records[0].WriteTo( bw, 0 ) ;				//TODO: No adjustment to 1st record
				for( int i=1 ; i < recordCount ; i++ )				//TODO: Remainder need adjustment
					result += records[i].WriteTo( bw, adjustment ) ;
				result += PutSwapped16( bw, 0 ) ;
				return result ;
			}
			public void Dump() {
				Trace.MsgLine( ToString() ) ;
			}
			public override string ToString() {
				return string.Format( "PalmFileHeader [size={0}] at filePosn 0x{1:x8}, {1}:\r\n", Size, Address ) +
					string.Format( "        databaseName: \"{0}\"\r\n",				Encoding.Default.GetString( databaseName,0,31 ) ) +
					string.Format( "          attributes:     0x{0:x4}, {0}\r\n",	attributes		) +
					string.Format( "             version:     0x{0:x4}, {0}\r\n",	version			) +
					string.Format( "         dateCreated: 0x{0:x8}, {0}\r\n",		dateCreated		) +
					string.Format( "        dateModified: 0x{0:x8}, {0}\r\n",		dateModified	) +
					string.Format( "        dateBackedup: 0x{0:x8}, {0}\r\n",		dateBackedup	) +
					string.Format( "        modification: 0x{0:x8}, {0}\r\n",		modification	) +
					string.Format( "           appInfoID: 0x{0:x8}, {0}\r\n",		appInfoID		) +
					string.Format( "          sortInfoID: 0x{0:x8}, {0}\r\n",		sortInfoID		) +
					string.Format( "                type: 0x{0:x8}, {0}\r\n",		type			) +
					string.Format( "             creator: 0x{0:x8}, {0}\r\n",		creator			) +
					string.Format( "        uniqueIDseed: 0x{0:x8}, {0}\r\n",		uniqueIDseed	) +
					string.Format( "    nextRecordListID: 0x{0:x8}, {0}\r\n",		nextRecordListID) +
					string.Format( "         recordCount:     0x{0:x4}, {0}\r\n",	recordCount		) +
					string.Format( "             padding:     0x{0:x4}, {0}\r\n",	padding			) ;
			}

			public PalmRecordInfo this[int index] {
				get {
					if( index < 0 || index >= recordCount ) throw new ArgumentException() ;
					return records[index] ;
				}
			}
		}
		protected const UInt32 NOT_USED = 0xffffffff ;

		protected string	fileName ;
		protected byte[]	fileData ;
		protected uint		filePosn ;

		protected PalmFileHeader	pfh ;

		public PalmFile( string fileName ) {
			LoadFrom( fileName ) ;
		}

		//TODO:
		//	WriteTo()
		//	This should write all the records to a new file, which means it would need "neat" ways of
		//	knowning when records have been replaced.  For the present, since we're only interested in
		//	the first record (the one that contains the MOBI/EXTH etc. data), we're going to "busk it".
		//
		public void LoadFrom( string fileName ) {
			this.fileName = fileName ;
			Trace.MsgLine( "Opening '{0}'", fileName ) ;
			fileData = File.ReadAllBytes( fileName ) ;
			Trace.MsgLine( "Read 0x{0:x8}, {0} bytes", fileData.Length ) ;

			filePosn = 0 ;
			pfh = new PalmFileHeader( fileData, ref filePosn ) ;
			pfh.Dump() ;
			//pfh[0].Dump() ;
			//pfh[1].Dump() ;
			for( int i=0 ; i < pfh.recordCount ; i++ ) {
#if false
				Trace.MsgLine( string.Format(
					"PDB[{0,3}]  FilePosn=[0x{1:x8}, {1,6}]  ATTR=[0x{2:x8}, {2,4}]  From1=[0x{3:x8}, {3,6}]  Len=[0x{4:x8}, {4,6}]",
					i,
					pfh[i].filePosn,
					pfh[i].id_attr,
					pfh[i].filePosn - pfh[1].filePosn,
					(i+1==pfh.recordCount ? (UInt32)fileData.Length : pfh[i+1].filePosn) - pfh[i].filePosn ) ) ;
#elif false
				DumpBytes(
					GetBytes(
						fileData,
						NonRef32(pfh[i].filePosn),
						(i+1==pfh.recordCount ? (UInt32)fileData.Length : pfh[i+1].filePosn) - pfh[i].filePosn ),
					string.Format( "PDB[{0,3}]", i ),
					pfh[i].filePosn ) ;
#endif
			}
			Trace.MsgLine() ;
			if( filePosn != pfh[0].filePosn ) {
				//Trace.MsgLine( "First PDB record does not follow header", filePosn, pfh[0].filePosn, 0 ) ;
				throw new MobiPositionException( "First PDB record does not follow header", filePosn, pfh[0].filePosn, 0 ) ;
			}
		}
	}

	public class MOBIfile : PalmFile {
		private struct PalmDOCHeader {
			public UInt16	compression ;
			public UInt16	unused ;
			public UInt32	textLength ;
			public UInt16	recordCount ;
			public UInt16	maxRecSize;
			public UInt16	encryption ;
			public UInt16	unknown ;

			public UInt32	Size				{ get { return 16 ; } }		//Fixed
			public UInt32	Address ;

			public UInt32 AddressOf( UInt32 offset ) {
				return this.Address + offset ;
			}

			public PalmDOCHeader( byte[] fileData, ref uint filePosn )
			:	this()
			{
				LoadFrom( fileData, ref filePosn ) ;
			}
			public void LoadFrom( byte[] fileData, ref uint filePosn ) {
				//Trace.MsgLine( "{0}.LoadFrom( {1:x8}, {1} )", this.GetType(), filePosn ) ;
				if( fileData == null ) throw new ArgumentException() ;
				if( fileData.Length - filePosn < Size ) throw new ArgumentException() ;

				Address = filePosn ;
				compression		= GetSwapped16( fileData, ref filePosn ) ;
				unused			= GetSwapped16( fileData, ref filePosn ) ;
				textLength		= GetSwapped32( fileData, ref filePosn ) ;
				recordCount		= GetSwapped16( fileData, ref filePosn ) ;
				maxRecSize		= GetSwapped16( fileData, ref filePosn ) ;
				encryption		= GetSwapped16( fileData, ref filePosn ) ;
				unknown			= GetSwapped16( fileData, ref filePosn ) ;
			}
			public UInt32 WriteTo( BinaryWriter bw ) {
				return	PutSwapped16( bw, compression	)
					+	PutSwapped16( bw, unused		)
					+	PutSwapped32( bw, textLength	)
					+	PutSwapped16( bw, recordCount	)
					+	PutSwapped16( bw, maxRecSize	)
					+	PutSwapped16( bw, encryption	)
					+	PutSwapped16( bw, unknown		) ;
			}
			public void Dump() {
				Trace.MsgLine( ToString() ) ;
			}
			public override string ToString() {
				return string.Format( "PalmDOCInfo [size={0}] at filePosn {1:x8}, {1}:\r\n", Size, Address ) +
					string.Format( "        compression:     0x{0:x4}, {0}\r\n",	compression		) +
					string.Format( "             unused: 0x{0:x8}, {0}\r\n",		unused			) +
					string.Format( "         textLength: 0x{0:x8}, {0}\r\n",		textLength		) +
					string.Format( "        recordCount:     0x{0:x4}, {0}\r\n",	recordCount		) +
					string.Format( "         maxRecSize:     0x{0:x4}, {0}\r\n",	maxRecSize		) +
					string.Format( "         encryption:     0x{0:x4}, {0}\r\n",	encryption		) +
					string.Format( "            unknown:     0x{0:x4}, {0}\r\n",	unknown			) ;
			}
		}

		/// <summary>
		/// PalmDoc header (first 16-bytes of PDB[0]).
		/// </summary>
		PalmDOCHeader	pdh ;
		/// <summary>
		/// MOBI header (follows PalmDoc header in PDB[0]).
		/// </summary>
		MOBIHeader		mh ;

		EXTHHeader		exth ;

		MobiDataBlock	gap1				= null ;	//	Between MOBI+EXTH and DRM/FullName
		MobiDataBlock	gap2				= null ;	//	Between DRM and FullName
		MobiDataBlock	gap3				= null ;	//	From DRM/FullName to end of record

		MobiDataBlock	drmData				= null ;
		MobiDataBlock	fullName			= null ;
		UInt32 fullNamePadding ;
		//byte[] remainder = null ;

//		byte[]			drmData				= null ;
//		byte[]			fullName			= null ;	//TODO: Should be a struct/class with the padding
//		UInt32			fullNamePadding		= 0 ;
//		byte[]			remainder			= null ;
		UInt32			UnreadRecordsPosn	= 0 ;		//What's not been read (record 1 onwards)

		public MOBIfile( string fileName )
		:	base( fileName )
		{
			LoadFrom( pfh[0].filePosn ) ;
		}

		public void LoadFrom( UInt32 filePosn ) {
			pdh = new PalmDOCHeader( fileData, ref filePosn ) ;
			pdh.Dump() ;
			mh = new MOBIHeader( fileData, ref filePosn ) ;
			mh.Dump() ;

			if( mh.headerLength != 228 &&
				mh.headerLength != 232 &&
				mh.headerLength != 248 &&
				mh.headerLength != 256 &&
				mh.headerLength != 264 ) {
				DumpBytes( GetBytes( fileData, NonRef32(filePosn), 128 ), "Bad-sized MOBIHeader" ) ;
			}

			exth = new EXTHHeader( fileData, ref filePosn ) ;
			exth.DumpAll() ;

			if( mh.HasDRM() && mh.drmOffset < mh.fullNameOffset ) {
				gap1 = new MobiDataBlock( fileData, ref filePosn, pfh[0].AddressOf( mh.drmOffset ) - filePosn, "GAP1" ) ;
				gap1.Dump() ;
				if( gap1.Length > 4 || !gap1.AllZero() ) {
					throw new MobiPositionException( "DRM data does not follow EXTH data", filePosn, pdh.Address, mh.drmOffset ) ;
				}
				drmData = new MobiDataBlock( fileData, ref filePosn, mh.drmSize, "DRM Data" ) ;
				drmData.Dump() ;
				gap2 = new MobiDataBlock( fileData, ref filePosn, pfh[0].AddressOf( mh.fullNameOffset ) - filePosn, "GAP2" ) ;
				gap2.Dump() ;
				fullName = new MobiDataBlock( fileData, ref filePosn, mh.fullNameLength, "FullName" ) ;
				fullName.Dump() ;
				if( fileData[filePosn++] != 0x00 || fileData[filePosn++] != 0x00 )
					throw new Exception( "Full name not followed by two 0x00 bytes" ) ;
				fullNamePadding = 2 + SkipToFourByteBoundary( ref filePosn ) ;
				Trace.MsgLine( "      Post-name pad: {0} bytes", fullNamePadding ) ;
				Trace.MsgLine() ;
				//TODO:move padding into fullname class
				gap3 = new MobiDataBlock( fileData, ref filePosn, pfh[1].filePosn - filePosn, "GAP3" ) ;
				gap3.Dump() ;
			} else {
				gap1 = new MobiDataBlock( fileData, ref filePosn, pfh[0].AddressOf( mh.fullNameOffset ) - filePosn, "GAP1" ) ;
				gap1.Dump() ;
				if( gap1.Length > 4 || !gap1.AllZero() ) {
					DumpBytes( GetBytes( fileData, NonRef32( filePosn - 0x80 ), 0x80 ), "Preceding 0x80 bytes", filePosn - 128 ) ;
					DumpBytes( GetBytes( fileData, NonRef32( filePosn        ), 0x80 ), "Following 0x80 bytes", filePosn       ) ;
					throw new MobiPositionException( "FullName does not follow EXTH/DRM data", filePosn, pdh.Address, mh.fullNameOffset ) ;
				}
				fullName = new MobiDataBlock( fileData, ref filePosn, mh.fullNameLength, "FullName" ) ;
				fullName.Dump() ;
				if( fileData[filePosn++] != 0x00 || fileData[filePosn++] != 0x00 )
					throw new Exception( "Full name not followed by two 0x00 bytes" ) ;
				fullNamePadding = 2 + SkipToFourByteBoundary( ref filePosn ) ;
				Trace.MsgLine( "      Post-name pad: {0} bytes", fullNamePadding ) ;
				Trace.MsgLine() ;
				//TODO:move padding into fullname class
				if( mh.HasDRM() ) {
					gap2 = new MobiDataBlock( fileData, ref filePosn, pfh[0].AddressOf( mh.drmOffset ) - filePosn, "GAP2" ) ;
					gap2.Dump() ;
					drmData = new MobiDataBlock( fileData, ref filePosn, mh.drmSize, "DRM Data" ) ;
					drmData.Dump() ;
				}
				gap3 = new MobiDataBlock( fileData, ref filePosn, pfh[1].filePosn - filePosn, "GAP3" ) ;
				gap3.Dump() ;
			}
#if false
			//--WEIRD----------------------------------------------------------------------------------//
			//	Some Amazon-generated, non-DRMed files have a 'drmOffset' of 3 .. treat as NOT_USED
			//
			if( mh.drmOffset == 3 )
				//TODO:!!!
				Trace.MsgLine( "---------------- drmOffset == 3 in {0} --------------------", fileName ) ;
			else if( mh.drmOffset != NOT_USED ) {
				if( pdh.AddressOf( mh.drmOffset ) != filePosn )
					throw new MobiPositionException( "DRM data does not follow EXTH data", filePosn, pdh.Address, mh.drmOffset ) ;
				drmData = GetBytes( fileData, ref filePosn, mh.drmSize ) ;
				DumpBytes( drmData, "DRM data", pdh.Address, mh.drmOffset ) ;
			}

			//TODO/WEIRD
			//	Calibre-generated MOBIs
			//	All Amazon-generated files (the only ones seen with a DRM block) have internal padding
			//	for the EXTH block, so we're [reasonably] safe complaining if the DRM data (if present)
			//	doesn't immediately follow the EXTH.

			//WEIRD
			//	Many of the Calibre-generated MOBI files have a four-byte gap
			if( pdh.AddressOf( mh.fullNameOffset ) == filePosn + 4 ) {
				UInt32 gap = GetSwapped32( fileData, ref filePosn ) ;
				if( gap == 0x0000 ) {
					Trace.MsgLine( "---------------- four-byte gap (padding={0}) -----------------", exth.nPadding ) ;
					DumpBytes( GetBytes( fileData, NonRef32( filePosn ), 0x80 ), "Following 0x80 bytes", filePosn       ) ;
				}
				else // Not 
					filePosn -= 4 ;
			}
			if( pdh.AddressOf( mh.fullNameOffset ) != filePosn ) {
				DumpBytes( GetBytes( fileData, NonRef32( filePosn - 0x80 ), 0x80 ), "Preceding 0x80 bytes", filePosn - 128 ) ;
				DumpBytes( GetBytes( fileData, NonRef32( filePosn        ), 0x80 ), "Following 0x80 bytes", filePosn       ) ;
				throw new MobiPositionException( "FullName does not follow EXTH/DRM data", filePosn, pdh.Address, mh.fullNameOffset ) ;
			}


			//Make this a struct/class to absorb reading/writing padding...
			//
			fullName = GetBytes( fileData, ref filePosn, mh.fullNameLength ) ;
			DumpBytes( fullName, "Full Name", pdh.Address, mh.fullNameOffset ) ;
			Trace.MsgLine( "          Full Name: \"{0}\"", Encoding.GetEncoding((int)mh.codePage).GetString( fullName ) ) ;
			if( fileData[filePosn++] != 0x00 || fileData[filePosn++] != 0x00 )
				throw new Exception( "Full name not followed by two 0x00 bytes" ) ;
			fullNamePadding = 2 + SkipToFourByteBoundary( ref filePosn ) ;
			Trace.MsgLine( "      Post-name pad: {0} bytes", fullNamePadding ) ;
			Trace.MsgLine() ;

			remainder = GetBytes( fileData, ref filePosn, pfh[1].filePosn - filePosn ) ;
			DumpBytes( remainder, "[??remainder??]", pdh.Address, filePosn - pdh.Address ) ;

			if( filePosn != pfh[1].filePosn ) throw new MobiPositionException( "Reading Record[0] does not take us to Record[1].", filePosn, pfh[1].filePosn, 0 ) ;
#endif
			UnreadRecordsPosn = filePosn ;
/*
			if( mobiOffset > pfh[0].offset + mh.fullNameOffset )
				throw new Exception( string.Format( "Gone beyond 'fullName': mobiOffset:{0}, Rec0Offset:{1}, fullNameOffset:{2}", mobiOffset, pfh[0].offset, mh.fullNameOffset ) ) ;
			UInt32 recZeroRemainderOffset =  mobiOffset - pfh[0].offset ;
			byte[] recZeroRemainder = GetBytes( mobiData, ref mobiOffset, (pfh[0].offset + mh.fullNameOffset) - mobiOffset ) ;
			DumpBytes( recZeroRemainder, string.Format( "RecordZeroRemainder @ {0:x8}, {0}", recZeroRemainderOffset ) ) ;
			Trace.MsgLine( "    mobiOffset: {0:x8}, {0}", mobiOffset ) ;
			Trace.MsgLine( "fullNameOffset: {0:x8}, {0}", mh.fullNameOffset ) ;
			Trace.MsgLine( "fullNameLength: {0:x8}, {0}", mh.fullNameLength ) ;
			Trace.MsgLine( " PDB[0].Offset: {0:x8}, {0}", pfh[0].offset ) ;
			Trace.MsgLine( " PDB[1].Offset: {0:x8}, {0}", pfh[1].offset ) ;
			Trace.MsgLine( " PDB[0].Length: {0:x8}, {0}", pfh[1].offset - pfh[0].offset ) ;
			byte[] fullName = GetBytes( mobiData, ref mobiOffset, mh.fullNameLength ) ;
			Trace.MsgLine( "     Full Name: \"{0}\"", Encoding.GetEncoding((int)mh.codePage).GetString( fullName ) ) ;
			mobiOffset += 2 ;	// two 0x00 bytes
			UInt32 fullNameEnd = mobiOffset ;
			while( (mobiOffset % 4) != 0 ) mobiOffset++ ;
			Trace.MsgLine( " Post-name pad: 2 + {0} bytes", mobiOffset - fullNameEnd ) ;
			Trace.MsgLine( "    mobiOffset: {0:x8}, {0}", mobiOffset ) ;
			Trace.MsgLine( "[??remainder??] {0:x8}, {0}", pfh[1].offset - mobiOffset ) ;

			Trace.MsgLine() ;
			DumpBytes( GetBytes( mobiData, ref mobiOffset, pfh[1].offset - mobiOffset ), "[??remainder??]" ) ;
*/
#if false
			{
				MobiDataBlock mdb = new MobiDataBlock() ;
				System.Xml.Serialization.XmlSerializer xsmdb = new System.Xml.Serialization.XmlSerializer( mdb.GetType() ) ;
				xsmdb.Serialize( Console.Out, mdb ) ;
			}
#endif
			for( int i=1 ; i < pfh.recordCount ; i++ ) {
				if( pfh[i-1].filePosn >= pfh[i].filePosn )
					Trace.MsgLine( "** PDB[{0}].offset:{1} >= PDB[{2}].offset:{3}", i-1, pfh[i-1].filePosn, i, pfh[i].filePosn ) ;
				if( pfh[i].filePosn - pfh[i-1].filePosn == 8 ) {
					UInt32 here = pfh[i-1].filePosn ;
					Trace.MsgLine( "** PDB[{0}].offset = {1:x8}, {1} is an 8-byte record", i-1, here ) ;
					if( Encoding.Default.GetString( GetBytes( fileData, ref here, 8 ) ).CompareTo( "BOUNDARY" ) == 0 ) {
						Trace.MsgLine( "** PDB[{0}] is BOUNDARY", i-1 ) ;
					}
				}
			}
		}

		/// <summary>
		/// Adjust the various size and offset members for consistency: this needs to be called
		/// after changes to EXTH strings and/or the FullTitle field, before everything can be
		/// written to a new file.
		/// </summary>
		protected UInt32 RebuildSizeAndOffsets( UInt32 orgZeroSize ) {
			UInt32 recZeroSize = 0 ;
			recZeroSize += pdh.Size ;
			recZeroSize += mh.Size ;
			recZeroSize += exth.RebuildSizeAndOffsets() ;
			if( mh.HasDRM() ) {
				mh.drmOffset = recZeroSize ;
				mh.drmSize   = (UInt32) drmData.Length ;
				recZeroSize += mh.drmSize ;
			}
			mh.fullNameOffset = recZeroSize ;
			mh.fullNameLength = (UInt32) fullName.Length ;
			recZeroSize += mh.fullNameLength ;
			fullNamePadding = 2 + PaddingNeededToFourByteBoundary( recZeroSize + 2 ) ;
			recZeroSize += fullNamePadding ;
			if( recZeroSize <= orgZeroSize )
				gap3.Replace( new byte[ orgZeroSize - recZeroSize ] ) ;
//				gap3.Length = orgZeroSize - recZeroSize ;
			recZeroSize += (UInt32) gap3.Length ;

			return recZeroSize ;		// New size for record zero
		}

		public UInt32 WriteTo( string fileName ) {
			//	TODO:	At present, the first two GAPs allowed for during read are not being written:
			//			except for a couple of unusual cases, the use of these has not been seen, but
			//			I probably ought to at least have the option of preserving them...
			//
			//	If the file already exists (we're editing "in place") then grab the
			//	various time-stamps so they can be restored afterwards (this will
			//	prevent books with edited details showing up as "New" on the Kindle).
			//
			DateTime createTime = DateTime.MinValue ;
			DateTime accessTime = DateTime.MinValue ;
			DateTime modifyTime = DateTime.MinValue ;
			if( File.Exists( fileName ) ) {
				accessTime = File.GetLastAccessTimeUtc( fileName ) ;
				modifyTime = File.GetLastWriteTimeUtc( fileName ) ;
				createTime = File.GetCreationTimeUtc( fileName ) ;
			}

			//	Work out the sizes for all the different headers and data chunks.
			//	The "adjustment" is the amount by which the first PDB record has grown/shrunk,
			//	and hence the adjustment need to the PDB header that locates all the remaining
			//	data records.
			//
			//	WARN:	This (and other code) assumes the PDB records are laid out in the file
			//			in their "natural" order... I've not seen it stated that this MUST be
			//			the case, and I've never seen an example of it NOT being the case, but
			//			if it ever WERE the case, things might break!
			//
			//	26.09.2014
			//	I've come to realise that the 'gap3' block is "padding" for the EXTH data to
			//	grow into without needing to rewrite the whole file.
			//
			UInt32 orgZeroSize = pfh[1].filePosn - pfh[0].filePosn ;	// Original size
			UInt32 recZeroSize = RebuildSizeAndOffsets( orgZeroSize ) ;
			UInt32 adjustment  = recZeroSize - orgZeroSize ;
			Trace.MsgLine( "Adjustment to remaining PDB records: {0}", (int)adjustment ) ;

			//	Open the file and begin writing bits and pieces...
			//
			BinaryWriter bw = null ;
			if( adjustment == 0 ) {
				bw = new BinaryWriter( File.Open( fileName, FileMode.Open ) ) ;
				Trace.MsgLine( "Replacing Palm/MOBI/EXTH data in: {0}", fileName ) ;
			} else {
				bw = new BinaryWriter( File.Open( fileName, FileMode.Create ) ) ;
				Trace.MsgLine( "Replacing whole file: {0}", fileName ) ;
			}
			UInt32 filePosn = 0 ;
			UInt32 written = 0 ;

			//	Palm File Header: this includes the PDB record positions, which
			//	will need to be adjusted by the amount calculated above.
			//
			written = pfh.WriteTo( bw, adjustment ) ;
			Trace.MsgLine( "@[0x{0:x8}, {0,8}] PalmFileHeader: {1,8} bytes", filePosn, written ) ;
			filePosn += written ;

			//	Check this has taken us to where we thought it should!
			//
			UInt32 recZeroPosn = pfh[0].filePosn ;
			if( recZeroPosn != filePosn ) throw new MobiPositionException( "About to write Doc/MOBI header at wrong position", filePosn, recZeroPosn, 0 ) ;

			//	Write the PalmDoc header
			//
			written = pdh.WriteTo( bw ) ;
			Trace.MsgLine( "@[0x{0:x8}, {0,8}] PalmDocHeader:  {1,8} bytes", filePosn, written ) ;
			filePosn += written ;

			//	Write the MOBI Header
			//
			written = mh.WriteTo( bw ) ;
			Trace.MsgLine( "@[0x{0:x8}, {0,8}] MOBI Header:    {1,8} bytes", filePosn, written ) ;
			filePosn += written ;

			//	Write the EXTH Header + records
			//
			//	TODO:	At present, this is always written with INTERNAL padding (matches what comes
			//			from Amazon): perhaps have options to force internal, external, or use whatever
			//			the original file used.
			//
			written = exth.WriteTo( bw ) ;
			Trace.MsgLine( "@[0x{0:x8}, {0,8}] EXTH data:      {1,8} bytes", filePosn, written ) ;
			filePosn += written ;

			//	Write the DRM data if originally present.
			//
			//	TODO:	All files seen have EXTH--DRM--TITLE in that order.  The reader probably should
			//			accept files that have a different order, but currently they are always written in
			//			this order.  Possibly have options to alter this.
			//
			if( mh.HasDRM() ) {
				if( recZeroPosn + mh.drmOffset != filePosn ) throw new MobiPositionException( "About to write DRM Data at wrong position", filePosn, recZeroPosn, mh.drmOffset ) ;
				if( mh.drmSize != drmData.Length ) throw new Exception( string.Format( "DRM Data wrong size: headerLength={0}, byteLength={1}", mh.drmSize, drmData.Length ) ) ;
				written = PutBytes( bw, drmData ) ;
				Trace.MsgLine( "@[0x{0:x8}, {0,8}] DRM data:       {1,8} bytes", filePosn, written ) ;
				filePosn += written ;
			}

			//	Write the "Full Title".
			//
			//	TODO:	Make a proper class for the full name and move the padding logic inside it.
			//
			if( recZeroPosn + mh.fullNameOffset != filePosn ) throw new MobiPositionException( "About to write Full Name at wrong position", filePosn, recZeroPosn, mh.fullNameOffset ) ;
			if( mh.fullNameLength != fullName.Length ) throw new Exception( string.Format( "Full Name wrong size: headerLength={0}, byteLength={1}", mh.fullNameLength, fullName.Length ) ) ;
			written = PutBytes( bw, fullName ) ;
			for( int i=0 ; i < fullNamePadding ; i++ )
				bw.Write( (byte) 0 ) ;
			written += fullNamePadding ;
			Trace.MsgLine( "@[0x{0:x8}, {0,8}] Full Name:      {1,8} bytes", filePosn, written ) ;
			filePosn += written ;

			//	Write any trailing data to the end of the record.  I've not seen any documented use of
			//	this, and while most/all files HAVE a trailer, they've always been full of zeros.  My
			//	guess is the space is added to allow room for the header to be edited without rewriting
			//	the whole file.
			//
			//	TODO:	Possibly make use of expansion space if present, and/or options to strip/add.
			//
			written = PutBytes( bw, gap3 ) ;
			Trace.MsgLine( "@[0x{0:x8}, {0,8}] Remainder:      {1,8} bytes", filePosn, written ) ;
			filePosn += written ;

			//	Check that we've got to the expected end of the first PDB record.
			//
			if( filePosn != pfh[1].filePosn + adjustment ) throw new MobiPositionException( "End of Doc/MOBI header at wrong position", filePosn, pfh[1].filePosn, 0 ) ;

			//	Write all the remaining PDB records in one swell foop UNLESS the adjustment was zero,
			//	in which case we've managed to squeeze everything into the original space (probably by
			//	altering the size of GAP3).
			//
			//	WARN:	This would break if the PDB[0] record wasn't physically the first in the file.
			//
			if( adjustment != 0 ) {
				written = PutBytes( bw, fileData, pfh[1].filePosn, (UInt32) fileData.Length - pfh[1].filePosn ) ;
				filePosn += written ;
				Trace.MsgLine( "Written {0} bytes", filePosn ) ;
			} else
				Trace.MsgLine( "Updated {0} bytes", filePosn ) ;

			bw.Close() ;

			//	If we've got some time-stampe, reapply them to the newly written file.
			//
			//	TODO:	Make this an option
			//
			if( createTime != DateTime.MinValue ) {
				Trace.MsgLine( "Resetting file times: C:{0}, A:{0}, M:{0}", createTime, accessTime, modifyTime ) ;
				File.SetCreationTimeUtc( fileName, createTime ) ;
				File.SetLastWriteTimeUtc( fileName, modifyTime ) ;
				File.SetLastAccessTimeUtc( fileName, accessTime ) ;
			}

			return filePosn ;
		}

		public string Title {
			get {
				string result = UpdatedTitle ;
				if( result != string.Empty )
					return result ;
				else
					return LongTitle ;
			}
			set {
				LongTitle		= value ;
				UpdatedTitle	= value ;
			}
		}
		public string LongTitle {
			get {
				return Encoding.GetEncoding( int.Parse( mh.codePage.ToString() ) ).GetString( fullName ) ;
			}
			set {
				fullName.Replace( Encoding.GetEncoding( int.Parse( mh.codePage.ToString() ) ).GetBytes( value ) ) ;
			}
		}
		public string FindFirstEXTH( uint type ) {
			for( int i=0 ; i < exth.recordCount ; i++ ) {
				if( exth.records[i].type == type ) {
					return Encoding.GetEncoding( int.Parse( mh.codePage.ToString() ) ).GetString( exth.records[i].data ) ;
				}
			}
			return string.Empty ;
		}
		public void SetFirstEXTH( uint type, string value ) {
			for( int i=0 ; i < exth.recordCount ; i++ ) {
				if( exth.records[i].type == type ) {
					exth.records[i].data = Encoding.GetEncoding(int.Parse( mh.codePage.ToString() ) ).GetBytes( value ) ;
					return ;
				}
			}
			throw new Exception( "Cannot add an EXTH that doesn't exist" ) ;
		}
		public string UpdatedTitle {
			get {
				return FindFirstEXTH( 503 ) ;
				for( int i=0 ; i < exth.recordCount ; i++ ) {
					if( exth.records[i].type == 503 ) {
						return Encoding.GetEncoding( int.Parse( mh.codePage.ToString() ) ).GetString( exth.records[i].data ) ;
					}
				}
				return string.Empty ;
			}
			set {
				SetFirstEXTH( 503, value ) ;
				/*
				for( int i=0 ; i < exth.recordCount ; i++ ) {
					if( exth.records[i].type == 503 ) {
						exth.records[i].data = Encoding.GetEncoding(int.Parse( mh.codePage.ToString() ) ).GetBytes( value ) ;
						//exth.records[i].length = 8 + (UInt32) exth.records[i].data.Length ;
					}
				}
				*/
			}
		}
		public string Author {
			get {
				return FindFirstEXTH( 100 ) ;
				for( int i=0 ; i < exth.recordCount ; i++ ) {
					if( exth.records[i].type == 100 ) {
						return Encoding.GetEncoding( int.Parse( mh.codePage.ToString() ) ).GetString( exth.records[i].data ) ;
					}
				}
				return string.Empty ;
			}
			set {
				SetFirstEXTH( 100, value ) ;
				/*
				for( int i=0 ; i < exth.recordCount ; i++ ) {
					if( exth.records[i].type == 100 ) {
						exth.records[i].data = Encoding.GetEncoding(int.Parse( mh.codePage.ToString() ) ).GetBytes( value ) ;
						break ; //TODO:ZXZ:Allow array selection...
					}
				}
				*/
			}
		}
		public string ASIN {
			get {
				return FindFirstEXTH( 113 ) ;
			}
			set {
				SetFirstEXTH( 113, value ) ;
			}
		}
		private struct MOBIHeader {
			//Note: "unknown_xxx" fields identify the offset within the PDB record (i.e. including the
			//		PalmDocHeader), NOT the offset within the MOBIHeader (this is how the documentation
			//		refers to them).
			//
			public MobiID		identifier ;		// "MOBI"
			public UInt32		headerLength ;		//	inc. above
			public UInt32		MOBItype ;
			public UInt32		codePage ;			// 1252/65001
			public UInt32		uniqueID ;
			public UInt32		fileVersion ;
			public UInt32		ortographicIndex ;
			public UInt32		inflectionIndex ;
			public UInt32		indexNames ;
			public UInt32		indexKeys ;
			public UInt32		extraIndex0 ;
			public UInt32		extraIndex1 ;
			public UInt32		extraIndex2 ;
			public UInt32		extraIndex3 ;
			public UInt32		extraIndex4 ;
			public UInt32		extraIndex5 ;
			public UInt32		firstNonBookIndex ;
			public UInt32		fullNameOffset ;	// From PDB-record zero
			public UInt32		fullNameLength ;	// From PDB-record zero
			public UInt32		locale ;			// LO=main (09=English); HI=dialect (08=UK, 04=US)
			public UInt32		inputLanguage ;
			public UInt32		outputLanguage ;
			public UInt32		minMobiVersion ;
			public UInt32		firstImageIndex ;
			public UInt32		huffmanRecordIndex ;
			public UInt32		huffmanRecordCount ;
			public UInt32		huffmanTableIndex ;
			public UInt32		huffmanTableLength ;
			public UInt32		EXTHflags ;			// 0x40 -> used
			public UInt64		unknown_132 ;		// 32 unknown bytes
			public UInt64		unknown_140 ;
			public UInt64		unknown_148 ;
			public UInt64		unknown_156 ;
			public UInt32		unknown_164 ;			// 0xffffffff ?
			public UInt32		drmOffset ;
			public UInt32		drmCount ;
			public UInt32		drmSize ;
			public UInt32		drmFlags ;
			public UInt64		unknown_184 ;		// bytes to end of MOBI header?
			public UInt16		firstContentRecord ;// normally 1
			public UInt16		lastContentRecord ;	// text+image+DATP+HUFF+DRM
			public UInt32		unknown_196 ;		// 0x00000001 ?
			public UInt32		FCISrecord ;
			public UInt32		FCIScount ;
			public UInt32		FLISrecord ;
			public UInt32		FLIScount ;
			public UInt64		unknown_216 ;		// 0x0000000000000000 ?
			public UInt32		unknown_224 ;		// 0xffffffff ?
			public UInt32		firstCDSRecord ;	// 0x00000000 ?
			public UInt32		CDScount ;			// 0xffffffff ?
			public UInt32		unknown_236 ;		// 0xffffffff ?
			public UInt32		ERDflags ;
			public UInt32		INDXRecord ;
			public UInt32		unknown_248 ;
			public UInt32		unknown_252 ;
			public UInt32		unknown_256 ;
			public UInt32		unknown_260 ;
			public UInt32		unknown_264 ;
			public UInt32		unknown_268 ;
			public UInt32		unknown_272 ;
			public UInt32		unknown_276 ;

			public UInt32		Size ;
			public UInt32		Address ;

			public MOBIHeader( byte[] fileData, ref uint filePosn ) : this() {
				LoadFrom( fileData, ref filePosn ) ;
			}
			public void LoadFrom( byte[] fileData, ref uint filePosn ) {
				//Trace.MsgLine( "{0}.LoadFrom( {1:x8}, {1} )", this.GetType(), filePosn ) ;
				if( fileData == null ) throw new ArgumentException() ;
				if( fileData.Length - filePosn < 8 ) throw new ArgumentException() ;

				Address = filePosn ;
				identifier			= GetSwapped32( fileData, ref filePosn ) ;
				if( identifier.ToString() != "MOBI" ) {//nonref
					DumpBytes( GetBytes( fileData, NonRef32( Address - 0x80 ), 0x80 ), "Preceding 0x80 bytes", Address - 128 ) ;
					DumpBytes( GetBytes( fileData, NonRef32( Address        ), 0x80 ), "Following 0x80 bytes", Address       ) ;
					Trace.MsgLine( ">>{0}<<", identifier.ToString() ) ;
					throw new Exception( "Invalid MOBI block" ) ;
				}
				headerLength		= GetSwapped32( fileData, ref filePosn ) ;
				if( fileData.Length - Address < headerLength ) throw new ArgumentException() ;
				MOBItype			= GetSwapped32( fileData, ref filePosn ) ;
				codePage			= GetSwapped32( fileData, ref filePosn ) ;
				uniqueID			= GetSwapped32( fileData, ref filePosn ) ;
				fileVersion			= GetSwapped32( fileData, ref filePosn ) ;
				ortographicIndex	= GetSwapped32( fileData, ref filePosn ) ;
				inflectionIndex		= GetSwapped32( fileData, ref filePosn ) ;
				indexNames			= GetSwapped32( fileData, ref filePosn ) ;
				indexKeys			= GetSwapped32( fileData, ref filePosn ) ;
				extraIndex0			= GetSwapped32( fileData, ref filePosn ) ;
				extraIndex1			= GetSwapped32( fileData, ref filePosn ) ;
				extraIndex2			= GetSwapped32( fileData, ref filePosn ) ;
				extraIndex3			= GetSwapped32( fileData, ref filePosn ) ;
				extraIndex4			= GetSwapped32( fileData, ref filePosn ) ;
				extraIndex5			= GetSwapped32( fileData, ref filePosn ) ;
				firstNonBookIndex	= GetSwapped32( fileData, ref filePosn ) ;
				fullNameOffset		= GetSwapped32( fileData, ref filePosn ) ;
				fullNameLength		= GetSwapped32( fileData, ref filePosn ) ;
				locale				= GetSwapped32( fileData, ref filePosn ) ;
				inputLanguage		= GetSwapped32( fileData, ref filePosn ) ;
				outputLanguage		= GetSwapped32( fileData, ref filePosn ) ;
				minMobiVersion		= GetSwapped32( fileData, ref filePosn ) ;
				firstImageIndex		= GetSwapped32( fileData, ref filePosn ) ;
				huffmanRecordIndex	= GetSwapped32( fileData, ref filePosn ) ;
				huffmanRecordCount	= GetSwapped32( fileData, ref filePosn ) ;
				huffmanTableIndex	= GetSwapped32( fileData, ref filePosn ) ;
				huffmanTableLength	= GetSwapped32( fileData, ref filePosn ) ;
				EXTHflags			= GetSwapped32( fileData, ref filePosn ) ;
				unknown_132			= GetSwapped64( fileData, ref filePosn ) ;
				unknown_140			= GetSwapped64( fileData, ref filePosn ) ;
				unknown_148			= GetSwapped64( fileData, ref filePosn ) ;
				unknown_156			= GetSwapped64( fileData, ref filePosn ) ;
				unknown_164			= GetSwapped32( fileData, ref filePosn ) ;
				drmOffset			= GetSwapped32( fileData, ref filePosn ) ;
				drmCount			= GetSwapped32( fileData, ref filePosn ) ;
				drmSize				= GetSwapped32( fileData, ref filePosn ) ;
				drmFlags			= GetSwapped32( fileData, ref filePosn ) ;
				unknown_184			= GetSwapped64( fileData, ref filePosn ) ;
				firstContentRecord	= GetSwapped16( fileData, ref filePosn ) ;
				lastContentRecord	= GetSwapped16( fileData, ref filePosn ) ;
				unknown_196			= GetSwapped32( fileData, ref filePosn ) ;
				FCISrecord			= GetSwapped32( fileData, ref filePosn ) ;
				FCIScount			= GetSwapped32( fileData, ref filePosn ) ;
				FLISrecord			= GetSwapped32( fileData, ref filePosn ) ;
				FLIScount			= GetSwapped32( fileData, ref filePosn ) ;
				unknown_216			= GetSwapped64( fileData, ref filePosn ) ;
				unknown_224			= GetSwapped32( fileData, ref filePosn ) ;
				firstCDSRecord		= GetSwapped32( fileData, ref filePosn ) ;
				CDScount			= GetSwapped32( fileData, ref filePosn ) ;
				unknown_236			= GetSwapped32( fileData, ref filePosn ) ;
				ERDflags			= GetSwapped32( fileData, ref filePosn ) ;
				if( headerLength > 228 ) {
					INDXRecord			= GetSwapped32( fileData, ref filePosn ) ;
					if( headerLength > 232 ) {
						unknown_248			= GetSwapped32( fileData, ref filePosn ) ;
						unknown_252			= GetSwapped32( fileData, ref filePosn ) ;
						unknown_256			= GetSwapped32( fileData, ref filePosn ) ;
						unknown_260			= GetSwapped32( fileData, ref filePosn ) ;
						if( headerLength > 248 ) {
							unknown_264			= GetSwapped32( fileData, ref filePosn ) ;
							unknown_268			= GetSwapped32( fileData, ref filePosn ) ;
							if( headerLength > 256 ) {
								unknown_272			= GetSwapped32( fileData, ref filePosn ) ;
								unknown_276			= GetSwapped32( fileData, ref filePosn ) ;
							}
						}
					}
				}
				Size = filePosn - Address ;
			}
			public UInt32 WriteTo( BinaryWriter bw ) {
				UInt32 result = 0 ;

				result = 0
					+	PutSwapped32( bw, identifier			)
					+	PutSwapped32( bw, headerLength			)
					+	PutSwapped32( bw, MOBItype				)
					+	PutSwapped32( bw, codePage				)
					+	PutSwapped32( bw, uniqueID				)
					+	PutSwapped32( bw, fileVersion 			)
					+	PutSwapped32( bw, ortographicIndex		)
					+	PutSwapped32( bw, inflectionIndex		)
					+	PutSwapped32( bw, indexNames			)
					+	PutSwapped32( bw, indexKeys				)
					+	PutSwapped32( bw, extraIndex0			)
					+	PutSwapped32( bw, extraIndex1			)
					+	PutSwapped32( bw, extraIndex2			)
					+	PutSwapped32( bw, extraIndex3			)
					+	PutSwapped32( bw, extraIndex4			)
					+	PutSwapped32( bw, extraIndex5			)
					+	PutSwapped32( bw, firstNonBookIndex		)
					+	PutSwapped32( bw, fullNameOffset		)
					+	PutSwapped32( bw, fullNameLength		)
					+	PutSwapped32( bw, locale				)
					+	PutSwapped32( bw, inputLanguage			)
					+	PutSwapped32( bw, outputLanguage		)
					+	PutSwapped32( bw, minMobiVersion		)
					+	PutSwapped32( bw, firstImageIndex		)
					+	PutSwapped32( bw, huffmanRecordIndex	)
					+	PutSwapped32( bw, huffmanRecordCount	)
					+	PutSwapped32( bw, huffmanTableIndex		)
					+	PutSwapped32( bw, huffmanTableLength	)
					+	PutSwapped32( bw, EXTHflags				)
					+	PutSwapped64( bw, unknown_132			)
					+	PutSwapped64( bw, unknown_140			)
					+	PutSwapped64( bw, unknown_148			)
					+	PutSwapped64( bw, unknown_156			)
					+	PutSwapped32( bw, unknown_164			)
					+	PutSwapped32( bw, drmOffset				)
					+	PutSwapped32( bw, drmCount				)
					+	PutSwapped32( bw, drmSize				)
					+	PutSwapped32( bw, drmFlags				)
					+	PutSwapped64( bw, unknown_184			)
					+	PutSwapped16( bw, firstContentRecord	)
					+	PutSwapped16( bw, lastContentRecord		)
					+	PutSwapped32( bw, unknown_196			)
					+	PutSwapped32( bw, FCISrecord			)
					+	PutSwapped32( bw, FCIScount				)
					+	PutSwapped32( bw, FLISrecord			)
					+	PutSwapped32( bw, FLIScount				)
					+	PutSwapped64( bw, unknown_216			)
					+	PutSwapped32( bw, unknown_224			)
					+	PutSwapped32( bw, firstCDSRecord		)
					+	PutSwapped32( bw, CDScount				)
					+	PutSwapped32( bw, unknown_236			)
					+	PutSwapped32( bw, ERDflags				) ;
				if( headerLength > 228 ) result += 0
					+	PutSwapped32( bw, INDXRecord			) ;
				if( headerLength > 232 ) result += 0
					+	PutSwapped32( bw, unknown_248			)
					+	PutSwapped32( bw, unknown_252			)
					+	PutSwapped32( bw, unknown_256			)
					+	PutSwapped32( bw, unknown_260			) ;
				if( headerLength > 248 ) result += 0
					+	PutSwapped32( bw, unknown_264			)
					+	PutSwapped32( bw, unknown_268			) ;
				if( headerLength > 256 ) result += 0
					+	PutSwapped32( bw, unknown_272			)
					+	PutSwapped32( bw, unknown_276			) ;

				return result ;
			}
			public void Dump() {
				Trace.MsgLine( ToString() ) ;
			}
			public override string ToString() {
				string result = string.Format( "MOBIHeader [size={0}] at filePosn {1:x8}, {1}:\r\n", Size, Address ) +
					string.Format( "         identifier: 0x{0:x8}, {0}\r\n",	identifier			) +
					string.Format( "       headerLength: 0x{0:x8}, {0}\r\n",	headerLength		) +
					string.Format( "           MOBItype: 0x{0:x8}, {0}\r\n",	MOBItype			) +
					string.Format( "           codePage: 0x{0:x8}, {0}\r\n",	codePage			) +
					string.Format( "           uniqueID: 0x{0:x8}, {0}\r\n",	uniqueID			) +
					string.Format( "        fileVersion: 0x{0:x8}, {0}\r\n",	fileVersion			) +
					string.Format( "   ortographicIndex: 0x{0:x8}, {0}\r\n",	ortographicIndex	) +
					string.Format( "    inflectionIndex: 0x{0:x8}, {0}\r\n",	inflectionIndex		) +
					string.Format( "         indexNames: 0x{0:x8}, {0}\r\n",	indexNames			) +
					string.Format( "          indexKeys: 0x{0:x8}, {0}\r\n",	indexKeys			) +
					string.Format( "        extraIndex0: 0x{0:x8}, {0}\r\n",	extraIndex0			) +
					string.Format( "        extraIndex1: 0x{0:x8}, {0}\r\n",	extraIndex1			) +
					string.Format( "        extraIndex2: 0x{0:x8}, {0}\r\n",	extraIndex2			) +
					string.Format( "        extraIndex3: 0x{0:x8}, {0}\r\n",	extraIndex3			) +
					string.Format( "        extraIndex4: 0x{0:x8}, {0}\r\n",	extraIndex4			) +
					string.Format( "        extraIndex5: 0x{0:x8}, {0}\r\n",	extraIndex5			) +
					string.Format( "  firstNonBookIndex: 0x{0:x8}, {0}\r\n",	firstNonBookIndex	) +
					string.Format( "     fullNameOffset: 0x{0:x8}, {0}\r\n",	fullNameOffset		) +
					string.Format( "     fullNameLength: 0x{0:x8}, {0}\r\n",	fullNameLength		) +
					string.Format( "             locale: 0x{0:x8}, {0}\r\n",	locale				) +
					string.Format( "      inputLanguage: 0x{0:x8}, {0}\r\n",	inputLanguage		) +
					string.Format( "     outputLanguage: 0x{0:x8}, {0}\r\n",	outputLanguage		) +
					string.Format( "     minMobiVersion: 0x{0:x8}, {0}\r\n",	minMobiVersion		) +
					string.Format( "    firstImageIndex: 0x{0:x8}, {0}\r\n",	firstImageIndex		) +
					string.Format( " huffmanRecordIndex: 0x{0:x8}, {0}\r\n",	huffmanRecordIndex	) +
					string.Format( " huffmanRecordCount: 0x{0:x8}, {0}\r\n",	huffmanRecordCount	) +
					string.Format( "  huffmanTableIndex: 0x{0:x8}, {0}\r\n",	huffmanTableIndex	) +
					string.Format( " huffmanTableLength: 0x{0:x8}, {0}\r\n",	huffmanTableLength	) +
					string.Format( "          EXTHflags: 0x{0:x8}, {0}\r\n",	EXTHflags			) +
					string.Format( "        unknown_132: 0x{0:x16}, {0}\r\n",	unknown_132			) +
					string.Format( "        unknown_140: 0x{0:x16}, {0}\r\n",	unknown_140			) +
					string.Format( "        unknown_148: 0x{0:x16}, {0}\r\n",	unknown_148			) +
					string.Format( "        unknown_156: 0x{0:x16}, {0}\r\n",	unknown_156			) +
					string.Format( "        unknown_164: 0x{0:x8}, {0}\r\n",	unknown_164			) +
					string.Format( "          drmOffset: 0x{0:x8}, {0}\r\n",	drmOffset			) +
					string.Format( "           drmCount: 0x{0:x8}, {0}\r\n",	drmCount			) +
					string.Format( "            drmSize: 0x{0:x8}, {0}\r\n",	drmSize				) +
					string.Format( "           drmFlags: 0x{0:x8}, {0}\r\n",	drmFlags			) +
					string.Format( "        unknown_184: 0x{0:x16}, {0}\r\n",	unknown_184			) +
					string.Format( " firstContentRecord:     0x{0:x4}, {0}\r\n",	firstContentRecord	) +
					string.Format( "  lastContentRecord:     0x{0:x4}, {0}\r\n",	lastContentRecord	) +
					string.Format( "        unknown_196: 0x{0:x8}, {0}\r\n",	unknown_196			) +
					string.Format( "         FCISrecord: 0x{0:x8}, {0}\r\n",	FCISrecord			) +
					string.Format( "          FCIScount: 0x{0:x8}, {0}\r\n",	FCIScount			) +
					string.Format( "         FLISrecord: 0x{0:x8}, {0}\r\n",	FLISrecord			) +
					string.Format( "          FLIScount: 0x{0:x8}, {0}\r\n",	FLIScount			) +
					string.Format( "        unknown_216: 0x{0:x16}, {0}\r\n",	unknown_216			) +
					string.Format( "        unknown_224: 0x{0:x8}, {0}\r\n",	unknown_224			) +
					string.Format( "     firstCDSRecord: 0x{0:x8}, {0}\r\n",	firstCDSRecord		) +
					string.Format( "           CDScount: 0x{0:x8}, {0}\r\n",	CDScount			) +
					string.Format( "        unknown_236: 0x{0:x8}, {0}\r\n",	unknown_236			) +
					string.Format( "           ERDflags: 0x{0:x8}, {0}\r\n",	ERDflags			) ;
				if( headerLength > 228 ) result +=
					string.Format( "         INDXRecord: 0x{0:x8}, {0}\r\n",	INDXRecord			) ;
				if( headerLength > 232 ) result +=
					string.Format( "        unknown_248: 0x{0:x8}, {0}\r\n",	unknown_248			) +
					string.Format( "        unknown_252: 0x{0:x8}, {0}\r\n",	unknown_252			) +
					string.Format( "        unknown_256: 0x{0:x8}, {0}\r\n",	unknown_256			) +
					string.Format( "        unknown_260: 0x{0:x8}, {0}\r\n",	unknown_260			) ;
				if( headerLength > 248 ) result +=
					string.Format( "        unknown_264: 0x{0:x8}, {0}\r\n",	unknown_264			) +
					string.Format( "        unknown_268: 0x{0:x8}, {0}\r\n",	unknown_268			) ;
				if( headerLength > 256 ) result +=
					string.Format( "        unknown_272: 0x{0:x8}, {0}\r\n",	unknown_272			) +
					string.Format( "        unknown_276: 0x{0:x8}, {0}\r\n",	unknown_276			) ;
				return result ;
			}

			public bool HasDRM() {
				//	If either the offset is NOT_USED (= -1) or the size is zero, there is no DRM.
				//
				if( drmOffset == NOT_USED || drmSize == 0 )
					return false ;
				//TODO:Remove...?
				//	Old hack for some Amazon books: should not be needed now we're looking at the size.
				//
				//if( drmOffset == 0x0003 )		//	Weird value seen when DRM not used
				//	return false ;
				return true ;
			}
		}
		private struct EXTHType {
			public	UInt32		type ;
			public	string		name ;
			public	bool		isBinary ;
			public EXTHType( UInt32 type, string name, bool isBinary ) {
				this.type		= type ;
				this.name		= name ;
				this.isBinary	= isBinary ;
			}
			public static EXTHType Find( UInt32 type ) {
				for( int i=0 ; i < EXTHTypeList.Length ; i++ ) {
					if( EXTHTypeList[i].type == type )
						return EXTHTypeList[i] ;
				}
				return new EXTHType( type, "[!!unknown!!]", true ) ;
			}
			public static EXTHType Find( string name ) {
				for( int i=0 ; i < EXTHTypeList.Length ; i++ ) {
					if( EXTHTypeList[i].name.Equals( name, StringComparison.CurrentCultureIgnoreCase ) )
						return EXTHTypeList[i] ;
				}
				return new EXTHType( 0xffffffff, "[!!unknown!!]", true ) ;
			}
		}
		static EXTHType[] EXTHTypeList = new EXTHType[] {
			new EXTHType( 1,	"drm_server_id",		false	),
			new EXTHType( 2,	"drm_commerce_id",		false	),
			new EXTHType( 3,	"drm_ebookbase_book_id",false	),

			new EXTHType( 100,	"Author",				false	),
			new EXTHType( 101,	"Publisher",			false	),
			new EXTHType( 102,	"Imprint",				false	),
			new EXTHType( 103,	"Description",			false	),
			new EXTHType( 104,	"ISBN",					false	),
			new EXTHType( 105,	"Subject",				false	),
			new EXTHType( 106,	"PublishingDate",		false	),
			new EXTHType( 107,	"Review",				false	),
			new EXTHType( 108,	"Contributor",			false	),
			new EXTHType( 109,	"Rights",				false	),
			new EXTHType( 110,	"SubjectCode",			false	),
			new EXTHType( 111,	"Type",					false	),
			new EXTHType( 112,	"Source",				false	),
			new EXTHType( 113,	"ASIN",					false	),
			new EXTHType( 114,	"VersionNumber",		true	),
			new EXTHType( 115,	"Sample",				true	),
			new EXTHType( 116,	"StartReading",			true	),
			new EXTHType( 117,	"Adult",				false	),
			new EXTHType( 118,	"RetailPrice",			false	),
			new EXTHType( 119,	"Currency",				false	),

			new EXTHType( 200,	"DictShortname",		false	),
			new EXTHType( 201,	"CoverOffset",			true	),
			new EXTHType( 202,	"ThumbOffset",			true	),
			new EXTHType( 203,	"hasFakeCover",			true	),
			new EXTHType( 204,	"CreatorSoftware",		true	),
			new EXTHType( 205,	"CreatorMajorVersion",	true	),
			new EXTHType( 206,	"creatorMinorVersion",	true	),
			new EXTHType( 207,	"creatorBuildNumber",	true	),
			new EXTHType( 208,	"Watermark",			true	),
			new EXTHType( 209,	"TamperProofKeys",		true	),
			
			new EXTHType( 300,	"FontSignature",		true	),

			new EXTHType( 401,	"ClippingLimit",		true	),
			new EXTHType( 402,	"PublisherLimit",		true	),
			new EXTHType( 403,	"[??unknown??]",		true	),
			new EXTHType( 404,	"TTSFlag",				true	),
			new EXTHType( 405,	"[??unknown??]",		true	),
			new EXTHType( 406,	"[??unknown??]",		true	),
			new EXTHType( 407,	"[??unknown??]",		true	),

			new EXTHType( 450,	"[??unknown??]",		true	),
			new EXTHType( 451,	"[??unknown??]",		true	),
			new EXTHType( 452,	"[??unknown??]",		true	),
			new EXTHType( 453,	"[??unknown??]",		true	),

			new EXTHType( 501,	"CDEContentType",		false	),
			new EXTHType( 502,	"LastUpdateTime",		false	),
			new EXTHType( 503,	"UpdatedTitle",			false	),
			new EXTHType( 504,	"CDEContentKey",		false	),
			
			new EXTHType( 524,	"Language",				false	),
		} ;
		private struct EXTHRecord {
			public UInt32		type ;
			public UInt32		length ;
			public byte[]		data ;

			public UInt32		Size ;
			public UInt32		Address ;

			public UInt32		dataLen			{ get{ return length - 8 ; } }

			public EXTHRecord( byte[] fileData, ref uint filePosn ) : this() {
				LoadFrom( fileData, ref filePosn ) ;
			}
			public void LoadFrom( byte[] fileData, ref uint filePosn ) {
				//Trace.MsgLine( "{0}.LoadFrom( {1:x8}, {1} )", this.GetType(), filePosn ) ;
				if( fileData == null ) throw new ArgumentException() ;
				if( fileData.Length - filePosn < 8 ) throw new ArgumentException() ;

				Address = filePosn ;
				type				= GetSwapped32( fileData, ref filePosn ) ;
				length				= GetSwapped32( fileData, ref filePosn ) ;
//???
//Trace.MsgLine( "EXTH: type=0x{0:x8}, {0,5}  length=0x{1:x8}, {1,5}", type, length ) ;
				data				= GetBytes( fileData, ref filePosn, dataLen ) ;
				Size = filePosn - Address ;
			}
			public UInt32 WriteTo( BinaryWriter bw ) {
				return	PutSwapped32( bw, type				)
					+	PutSwapped32( bw, length			)
					+	PutBytes( bw, data ) ;
			}
			public UInt32 RebuildSizeAndOffsets() {
				length = 8 + (UInt32) data.Length ;
				return length ;
			}
			public void Dump() {
				Trace.MsgLine( ToString() ) ;
			}
			public override string ToString() {
				EXTHType exth = EXTHType.Find( type ) ;
				string result = string.Format( "EXTH [{1,21}-{0,3}] [{2,4}] ", type, exth.name, dataLen ) ;
				if( exth.isBinary ) {
					UInt32 zero = 0 ;
					switch( dataLen ) {
						case 4:	return result + string.Format( "0x{0:x8}, {0}", GetSwapped32( this.data, ref zero ) ) ;
						case 8:	return result + string.Format( "0x{0:x16}, {0}", GetSwapped64( this.data, ref zero ) ) ;
						default:
							for( int i=0 ; i < dataLen ; i++ )
								result += string.Format( "{0:x2}.", data[i] ) ;
							return result ;
					}
				} else {
					return result + string.Format( "\"{0}\"", Encoding.Default.GetString(data) ) ;
				}
			}
		}
		private struct EXTHHeader {
			public MobiID		identifier ;		// "EXTH"
			public UInt32		headerLength ;		//	inc. above
			public UInt32		recordCount ;
			public EXTHRecord[]records ;
			public UInt32		nPadding ;

			public UInt32		Size ;
			public UInt32		Address ;

			public EXTHHeader( byte[] fileData, ref uint filePosn ) : this() {
				LoadFrom( fileData, ref filePosn ) ;
			}
			public void LoadFrom( byte[] fileData, ref uint filePosn ) {
				//Trace.MsgLine( "{0}.LoadFrom( {1:x8}, {1} )", this.GetType(), filePosn ) ;
				if( fileData == null ) throw new ArgumentException() ;
				if( fileData.Length - filePosn < 12 ) throw new ArgumentException() ;

				Address = filePosn ;
				identifier			= GetSwapped32( fileData, ref filePosn ) ;
				if( identifier.ToString() != "EXTH" ) {//nonref
					DumpBytes( GetBytes( fileData, NonRef32( Address - 0x80 ), 0x80 ), "Preceding 0x80 bytes", Address - 128 ) ;
					DumpBytes( GetBytes( fileData, NonRef32( Address        ), 0x80 ), "Following 0x80 bytes", Address       ) ;
					Trace.MsgLine( ">>{0}<<", identifier.ToString() ) ;
					throw new Exception( "Invalid EXTH block" ) ;
				}
				headerLength		= GetSwapped32( fileData, ref filePosn ) ;
				if( fileData.Length - filePosn < headerLength - 12 ) throw new ArgumentException() ;
				recordCount			= GetSwapped32( fileData, ref filePosn ) ;
//recordCount++;//ZXZ!!!
//???
//DumpBytes( GetBytes( fileData, NonRef32( Address ), 0x100 ), "EXTH Header", Address       ) ;
				records = new EXTHRecord[recordCount] ;
				for( int i=0 ; i < recordCount ; i++ )
					records[i].LoadFrom( fileData, ref filePosn ) ;

				//WEIRD
				//	The "spec" on MobiRead says that the EXTH block is followed by zero-byte padding
				//	upto a four-byte boundary.  It further says:
				//	a)	That this padding is not included in the EXTH's 'headerLength' field;
				//	b)	No padding is needed if already at a four-byte boundary.
				//	Despite this, the following has been observed:
				//	a)	All my Amazon .AZWs include the padding in 'headerLength';
				//	b)	None (?ZXZ) of my Calibre-generated .MOBIs included padding;
				//	c)	The Perl 'mobi2mobi' utility complains if it isn't included.

				Size = filePosn - Address ;
				nPadding = SkipToFourByteBoundary( ref filePosn ) ;

				if( Size == headerLength && nPadding == 0 ) {
					Trace.MsgLine( "EXTH: Padding=None" ) ;
				} else if( Size + nPadding == headerLength ) {
					Trace.MsgLine( "EXTH: Padding=Internal: Size={0}, headerLength={1}, padding={2}", Size, headerLength, nPadding ) ;
					Size += nPadding ;
				} else if( Size == headerLength ) {
					Trace.MsgLine( "EXTH: Padding=EXTERNAL: Size={0}, headerLength={1}, padding={2}", Size, headerLength, nPadding ) ;
				} else
//ZXZ!!!			Trace.MsgLine( "Invalid EXTH padding: Size={0}, headerLength={1}, padding={2}, diff={3}", Size, headerLength, nPadding, (int)Size-headerLength ) ;
					throw new Exception( string.Format( "Invalid EXTH padding: Size={0}, headerLength={1}, padding={2}, diff={3}", Size, headerLength, nPadding, (int)Size-headerLength ) ) ;
				Trace.MsgLine() ;
			}
			public UInt32 WriteTo( BinaryWriter bw ) {
				UInt32 result ;
				result = 0
					+	PutSwapped32( bw, identifier	)
					+	PutSwapped32( bw, headerLength	)
					+	PutSwapped32( bw, recordCount	) ;
				for( int i=0 ; i < recordCount ; i++ )
					result += records[i].WriteTo( bw ) ;
				for( int i=0 ; i < nPadding ; i++ )
					bw.Write( (byte) 0 ) ;
				result += nPadding ;
				return result ;
			}
			public UInt32 RebuildSizeAndOffsets() {
				UInt32 result = 12 ;
				for( int i=0 ; i < recordCount ; i++ )
					result += records[i].RebuildSizeAndOffsets() ;
#if UseExternalPadding
				headerLength = Size = result ;
				nPadding = PaddingNeededToFourByteBoundary( headerLength ) ;
				return headerLength + nPadding ;
#else
				nPadding = PaddingNeededToFourByteBoundary( result ) ;
				result += nPadding ;
				headerLength = Size = result ;
				return Size ;
#endif
			}
			//---------------------------------------
			//TODO: Find/replace/delete EXTH records
			//---------------------------------------
			public void Dump() {
				Trace.MsgLine( ToString() ) ;
			}
			public void DumpAll() {
				Dump() ;
				for( int i=0 ; i < recordCount ; i++ )
					records[i].Dump() ;
				Trace.MsgLine() ;
			}
			public override string ToString() {
				return string.Format( "EXTHHeader [size={0}] at filePosn {1:x8}, {1}:\r\n", Size, Address ) +
					string.Format( "         identifier: 0x{0:x8}, {0}\r\n",	identifier			) +
					string.Format( "       headerLength: 0x{0:x8}, {0}\r\n",	headerLength		) +
					string.Format( "        recordCount: 0x{0:x8}, {0}\r\n",	recordCount			) +
					string.Format( "           nPadding: 0x{0:x8}, {0}\r\n",	nPadding			) ;
			}
		}
		public void DumpXML() {
			TripeXmlDocument xml = new TripeXmlDocument() ;
			//TripeNode ebook = new TripeNode( xml.AppendChild( xml.CreateElement( "ebook" ) ) ) ;
			TripeNode ebook =	new TripeNode( xml, "ebook" ) ;
			ebook	.AddChild( "filename",		this.fileName		)
					;
			TripeNode palmFile =new TripeNode( ebook, "palmfile" ) ;
			TripeNode palmDoc = new TripeNode( ebook, "palmdoc" ) ;
			TripeNode mobi =	new TripeNode( ebook, "mobi" ) ;
			TripeNode exth =	new TripeNode( ebook, "exth" ) ;
			palmFile.AddChild( "databaseName",	pfh.databaseName	)
					.AddChild( "uniqueIDseed",	pfh.uniqueIDseed	)
					;
			palmDoc	.AddChild( "textLength",	pdh.textLength		)
					.AddChild( "recordCount",	pdh.recordCount		)
					;
			mobi	.AddChild( "codePage",		mh.codePage			)
					.AddChild( "uniqueID",		mh.uniqueID			)
					.AddChild( "fileVersion",	mh.fileVersion		)
					;
			exth	.AddChild( "author",		Author				)
					.AddChild( "title",			Title				)
					.AddChild( "asin",			ASIN				)
					;
			Console.WriteLine( "{0}", xml.OuterXml ) ;
		}
	}

	class TripeNode
	{
		private XmlNode node ;
		public TripeNode( XmlNode node ) {
			this.node = node ;
		}
		public TripeNode( XmlDocument doc, String element ) {
			this.node = doc.AppendChild( doc.CreateElement( element ) ) ;
		}
		public TripeNode( TripeNode parent, String element ) {
			XmlDocument doc = parent.node.OwnerDocument ;
			this.node = parent.node.AppendChild( doc.CreateElement( element ) ) ;
		}
		public TripeNode AddChild( XmlNode child ) {
			node.AppendChild( child ) ;
			return this ;
		}
		public TripeNode AddChild( String tag, String value ) {
			XmlDocument doc = node.OwnerDocument ;
			node.AppendChild( doc.CreateElement( tag ).AppendChild( doc.CreateTextNode( value ) ).ParentNode ) ;
			return this ;
		}
		public TripeNode AddChild( String tag, byte[] value ) {
			return AddChild( tag, Encoding.UTF8.GetString( value ) ) ;
		}
		public TripeNode AddChild( String tag, long value ) {
			return AddChild( tag, value.ToString() ) ;
		}
	}
	class TripeXmlDocument : XmlDocument
	{
		public XmlNode NewTag( String tag, byte[] value ) {
			return NewTag( tag, Encoding.UTF8.GetString( value ) ) ;
		}
		public XmlNode NewTag( String tag, String value ) {
			return CreateElement( tag ).AppendChild( CreateTextNode( value ) ).ParentNode ;
		}
	}

	class Program
	{
		static int Main( string[] args )
		{
			
#if __STREAM_CHUNK_TESTING
			byte[] buffer = Encoding.ASCII.GetBytes( "abcdefghijklm" ) ;
			MemoryStream ms = new MemoryStream( buffer );
			MemoryStream sms = new MemoryStream( buffer, 5, 5 );
			StreamChunk sc = new StreamChunk( ms, 5, 5 );

			int offset = 0;
			int count  = 6;

			Console.WriteLine( "Using memory stream" );
			try
			{
				Console.WriteLine( "Before:" + Encoding.ASCII.GetString( buffer ) );
				sms.Write( Encoding.ASCII.GetBytes( "VWXYZ@" ), offset, count );
				Console.WriteLine( " After:" + Encoding.ASCII.GetString( buffer ) );
			}
			catch( Exception ex )
			{
				Console.WriteLine( "Exception: " + ex.GetType() + ": " + ex.Message );
			}

			ms.Position = 0;
			ms.Write( Encoding.ASCII.GetBytes( "abcdefghijklm" ), 0, buffer.Length );
			Console.WriteLine( "Using stream chunk" );
			try
			{
				sc.Position = 0;
				Console.WriteLine( "Before:" + Encoding.ASCII.GetString( buffer ) );
				sc.Write( Encoding.ASCII.GetBytes( "VWXYZ@" ), offset, count );
				Console.WriteLine( " After:" + Encoding.ASCII.GetString( buffer ) );
			}
			catch( Exception ex )
			{
				Console.WriteLine( "Exception: " + ex.GetType() + ": " + ex.Message );
			}
			Console.ReadLine();
#else
			bool dumpOnly = Environment.GetEnvironmentVariable( "TripeMobiDump" ) != null ;
			bool dumpXML = false ;
			int argp = 0 ;

			if( args.Length == 0 ) {
				Trace.MsgLine( "usage: TripeMobi [-v][-d] <mobifile>" ) ;
				return 1 ;
			}

			try {
				for( argp = 0 ; args[argp][0] == '-' ; argp++ ) {
					switch( args[argp] ) {
						case "-v":
							Utils.Trace.ConsoleOut = true ;
							break ;
						case "-d":
							dumpOnly = true ;
							Utils.Trace.ConsoleOut = true ;
							break ;
						case "-x":
							dumpXML = true ;
							dumpOnly = true ;
							break ;
						default:
							throw new Exception( String.Format( "Unrecognised option '%s'", args[argp] ) );
					}
				}

				MOBIfile mobi = new MOBIfile( args[argp] );
				if( dumpXML )
					mobi.DumpXML() ;

				if( !dumpOnly ) {
					TripeMobi.EditDlg dlg = new TripeMobi.EditDlg();
					dlg.txtAuthor.Text	= mobi.Author;
					dlg.txtTitle.Text	= mobi.Title ;
					if( dlg.ShowDialog() == DialogResult.OK ) {
						if( (mobi.Author != dlg.txtAuthor.Text) || (mobi.Title != dlg.txtTitle.Text) ) {
							mobi.Author = dlg.txtAuthor.Text ;
							mobi.Title	= dlg.txtTitle.Text ;
							mobi.WriteTo( args[argp] ) ;
						}
					}
				}
			} catch( Exception e ) {
				throw new Exception( string.Format( "Processing: {0}\r\nException: {1}\r\n{2}", args[argp], e.Message, e.StackTrace ) ) ;
			}
#endif
			return 0 ;
		}
	}
}
