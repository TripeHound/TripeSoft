using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace TripeMobi {
	public partial class EditDlg : Form {
		public EditDlg() {
			InitializeComponent();
		}

		private void LastFirst_Click( object sender, EventArgs e ) {
			if( txtAuthor.Text.Contains( "," ) ) {
				string[] bits = txtAuthor.Text.Split( ',' ) ;
				txtAuthor.Text = bits[1].Trim() + " " + bits[0] ;
			} else {
				string[] bits = txtAuthor.Text.Split( ' ' ) ;
				if( bits.Length > 1 ) {
					txtAuthor.Text = bits[bits.Length - 1] + "," ;
					for( int i=0 ; i < bits.Length - 1 ; i++ )
						txtAuthor.Text += " " + bits[i] ;
				}
			}
			txtAuthor.Focus() ;
		}
	}
}