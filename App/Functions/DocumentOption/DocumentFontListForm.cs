﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BrightIdeasSoftware;
using iTextSharp.text.pdf;
using PDFPatcher.Common;
using PDFPatcher.Model;
using PDFPatcher.Processor;

namespace PDFPatcher.Functions
{
	public partial class DocumentFontListForm : Form
	{
		Dictionary<int, string> _fontIdNames;
		Dictionary<string, PageFont> _pageFonts;
		internal FontSubstitutionsEditor SubstitutionsEditor { get; set; }

		public IList<string> SelectedFonts {
			get {
				var sf = new List<string>();
				foreach (PageFont item in _FontListBox.CheckedObjects) {
					sf.Add(item.Name);
				}
				return sf;
			}
		}

		public DocumentFontListForm() {
			InitializeComponent();
			this.SetIcon(Properties.Resources.Fonts);
			Load += (s, args) => {
				MinimumSize = Size;
				if (AppContext.Recent.SourcePdfFiles.HasContent()) {
					_SourceFileBox.FileList.Text = AppContext.Recent.SourcePdfFiles[0];
				}
			};
			_Worker.ProgressChanged += (s, args) => {
				if (args.ProgressPercentage < 0) {
					_ProgressBar.Maximum = -args.ProgressPercentage;
				}
				else if (args.ProgressPercentage > 0) {
					_ProgressBar.SetValue(args.ProgressPercentage);
				}
				else {
					if (args.UserState is PageFont pf) {
						_FontListBox.AddObject(pf);
					}
				}
			};
			_Worker.RunWorkerCompleted += (s, args) => {
				_ProgressBar.Value = _ProgressBar.Maximum;
				if (_pageFonts.HasContent()) {
					_FontListBox.AddObjects(_pageFonts.Values);
				}
				_ListFontsButton.Enabled = true;
			};
			_Worker.DoWork += (s, args) => {
				try {
					_fontIdNames = new Dictionary<int, string>();
					_pageFonts = new Dictionary<string, PageFont>();
					_FontListBox.ClearObjects();
					using (var p = PdfHelper.OpenPdfFile(_SourceFileBox.FirstFile, false, false)) {
						var r = PageRangeCollection.Parse(_PageRangeBox.Text, 1, p.NumberOfPages, true);
						var pp = new int[p.NumberOfPages + 1];
						_Worker.ReportProgress(-r.TotalPages);
						int i = 0;
						foreach (var range in r) {
							foreach (var page in range) {
								if (_Worker.CancellationPending) {
									return;
								}
								_Worker.ReportProgress(++i);
								if (pp[page] != 0) {
									continue;
								}
								pp[page] = 1;
								GetPageFonts(p, page);
							}
						}
					}
				}
				catch (Exception ex) {
					FormHelper.ErrorBox(ex.Message);
				}
			};
			_FontListBox.PersistentCheckBoxes = true;
			new TypedColumn<PageFont>(_NameColumn) {
				AspectGetter = (o) => o.Name
			};
			new TypedColumn<PageFont>(_FirstPageColumn) {
				AspectGetter = (o) => o.FirstPage
			};
			new TypedColumn<PageFont>(_EmbeddedColumn) {
				AspectGetter = (o) => o.Embedded
			};
			new TypedColumn<PageFont>(_ReferenceColumn) {
				AspectGetter = (o) => o.Reference
			};
		}

		private void GetPageFonts(PdfReader pdf, int pageNumber) {
			var page = pdf.GetPageNRelease(pageNumber);
			var fl = page.Locate<PdfDictionary>(true, PdfName.RESOURCES, PdfName.FONT);
			if (fl == null) {
				return;
			}
			foreach (var item in fl) {
				var fr = item.Value as PdfIndirectReference;
				if (fr == null) {
					continue;
				}
				if (_fontIdNames.TryGetValue(fr.Number, out string fn)) {
					_pageFonts[fn].IncrementReference();
					continue;
				}
				if (PdfReader.GetPdfObjectRelease(fr) is PdfDictionary f) {
					var bf = f.GetAsName(PdfName.BASEFONT);
					if (bf == null) {
						continue;
					}
					fn = PdfHelper.GetPdfNameString(bf, AppContext.Encodings.FontNameEncoding); // 字体名称
					fn = PdfDocumentFont.RemoveSubsetPrefix(fn);
					_fontIdNames.Add(fr.Number, fn);
					if (_pageFonts.TryGetValue(fn, out PageFont pf)) {
						pf.IncrementReference();
						continue;
					}
					_pageFonts.Add(fn, new PageFont(fn, pageNumber, PdfDocumentFont.HasEmbeddedFont(f)));
				}
			}
		}

		private void SetGoal(int goal) { _ProgressBar.Maximum = goal; }
		private void _ListFontsButton_Click(object sender, EventArgs e) {
			_ProgressBar.Value = 0;
			_ListFontsButton.Enabled = false;
			_Worker.RunWorkerAsync();
		}

		sealed class PageFont
		{
			public string Name { get; }
			public int FirstPage { get; }
			public int Reference { get; private set; }
			public bool Embedded { get; set; }

			public PageFont(string name, int firstPage, bool embedded) {
				Name = name;
				FirstPage = firstPage;
				Embedded = embedded;
				Reference = 1;
			}

			public void IncrementReference() {
				Reference++;
			}
		}

		private void _SelectAllButton_Click(object sender, EventArgs e) {
			if (_FontListBox.GetItemCount() == 0) {
				return;
			}
			if (_FontListBox.GetItem(0).Checked == false) {
				_FontListBox.CheckObjects(_FontListBox.Objects);
			}
			else {
				_FontListBox.CheckedObjects = null;
			}
			_FontListBox.Focus();
		}

		private void _AddSelectedFontsButton_Click(object sender, EventArgs e) {
			if (SubstitutionsEditor == null) {
				return;
			}
			var sf = SelectedFonts;
			if (sf.Count == 0) {
				FormHelper.ErrorBox("请选择需要添加到替换列表的字体。");
				return;
			}
			SubstitutionsEditor.AddFonts(sf);
			Close();
		}

		private void _AppConfigButton_Click(object sender, EventArgs e) {
			this.ShowDialog<AppOptionForm>();
		}
	}
}
