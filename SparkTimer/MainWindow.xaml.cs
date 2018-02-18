using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SparkTimer
{
    /// <inheritdoc>
    ///     <cref></cref>
    /// </inheritdoc>
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private int _width;
        private int _height;
        private int _layerCount;
        private float _accumulatedSeconds;
        private bool _hasErrors;
        private List<byte[]> _layers;

        private BackgroundWorker _processFile = new BackgroundWorker();

        public MainWindow()
        {
            InitializeComponent();
            _hasErrors = false;
            _layers = new List<byte[]>();
            _processFile.WorkerSupportsCancellation = true;
            _processFile.WorkerReportsProgress = true;

            _processFile.DoWork += new DoWorkEventHandler(processFile_DoWork);
            _processFile.ProgressChanged +=
                new ProgressChangedEventHandler(processFile_ProgressChanged);
            _processFile.RunWorkerCompleted +=
                new RunWorkerCompletedEventHandler(processFile_RunWorkerCompleted);


            pbFileRead.Value = 0;
        }

        private void processFile_DoWork(object sender, DoWorkEventArgs e)
        {
            if(!(sender is BackgroundWorker worker)) return;

            var file = (string) e.Argument;

            try
            {
                var lineCount = File.ReadLines(file).Count();
                var currentline = 0;

                // Open the text file using a stream reader.
                using (var sr = new StreamReader(file))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        currentline++;
                        var progress = 100 * ((float)currentline / (float)lineCount);
                        worker.ReportProgress((int)progress);

                        if (line.StartsWith("{{"))
                        {
                            line = sr.ReadLine();
                            _layers.Add(Encoding.ASCII.GetBytes(line));

                            while ((line = sr.ReadLine()) != null)
                            {
                                if (line.StartsWith("}}"))
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            line = line.ToUpper(); // Shouldn't be needed, but also shouldn't hurt

                            var chars = line.ToCharArray();

                            switch (chars[0])
                            {
                                case ';':
                                    {
                                        //comment
                                        if (chars[1] == 'W')
                                        {
                                            _width = GetCommentInt(line);
                                        }
                                        else if (chars[1] == 'H')
                                        {
                                            _height = GetCommentInt(line);
                                        }
                                        else if (chars[1] == 'L')
                                        {
                                            _layerCount = GetCommentInt(line);
                                        }

                                        break;
                                    }
                                case 'G':
                                    //G code
                                    if (chars[1] == '4' && chars[2] == ' ')
                                    {
                                        _accumulatedSeconds += GetG4(line);
                                    }
                                    else if (chars[1] == '1' && chars[2] == ' ')
                                    {
                                        var a = GetG1MoveTime(line);
                                        _accumulatedSeconds += a;
                                    }
                                    break;
                                case 'M':
                                    //M code
                                    break;
                                default:
                                    var str =
                                        $"{line.ToCharArray()[0]}:{Convert.ToUInt32(line.ToCharArray()[0]):X} ";

                                    Console.WriteLine(str);
                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                _hasErrors = true;
                Console.WriteLine(Properties.Resources.OnDrop_FileCouldNotBeRead);
                Console.WriteLine(error.Message);
            }

            worker.ReportProgress(100);
        }

        private void processFile_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            pbFileRead.Value = e.ProgressPercentage;
        }

        private void processFile_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (_hasErrors)
            {
                lblError.Content = "There were errors reading the file, so the time estimate may not be accurate.";
            }

            if (_layerCount > 0)
            {
                slLayer.IsEnabled = true;
            }

            slLayer.Maximum = _layerCount;
            slLayer.Minimum = 1;
            GenerateBitmap(0);

            UpdateTimeDisplay();
            UpdateLayerCountDisplay();
            UpdateHeightDisplay();
            UpdateWidthDisplay();

            Console.WriteLine(Properties.Resources.OnDrop_Done);
        }

        private void GbFile_OnDrop(object sender, DragEventArgs e)
        {
            lblError.Content = "";

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            // Note that you can have more than one file.
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files is null)
            {
                return;
            }

            if (files.Length <= 0)
            {
                return;
            }

            var file = files[0];
            if (string.IsNullOrEmpty(file))
            {
                MessageBox.Show("That file was empty!");
                return;
            }

            _processFile.RunWorkerAsync(file);
        }

        private void GenerateBitmap(int layerIdx)
        {
            if(layerIdx > _layers.Count) return;
            try
            {
                var layer = _layers[layerIdx];

                var image = new Bitmap(_width, _height);
                var bitmapData = image.LockBits(
                    new Rectangle(0, 0, _width, _height),
                    ImageLockMode.ReadWrite,
                    PixelFormat.Format1bppIndexed);
                    
                var idx = 0;

                var scan = new byte[(_width + 7) / 8];

                for (var y = 0; y < _height; y++)
                {
                    Array.Copy(layer, idx ,scan, 0, scan.Length);
                    idx += scan.Length;
                    for (var b = 0; b < scan.Length; b++)
                    {
                        scan[b] = ReverseWithLookupTable(scan[b]);
                    }
                    Marshal.Copy(scan, 0, (IntPtr)((long)bitmapData.Scan0 + bitmapData.Stride * y), scan.Length);
                }

                image.UnlockBits(bitmapData);

                image.RotateFlip(RotateFlipType.Rotate90FlipNone);

                using (var memory = new MemoryStream())
                {
                    image.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                    memory.Position = 0;
                    var bitmapimage = new BitmapImage();
                    bitmapimage.BeginInit();
                    bitmapimage.StreamSource = memory;
                    bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapimage.EndInit();

                    imgLayer.Source = bitmapimage;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error");
            }
        }

        private void UpdateHeightDisplay()
        {
            //Yes, this is reversed, height = horizontal
            lblHeight.Content = $"{Properties.Resources.OnDrop_HorizontalPixels}: {_height}";
        }

        private void UpdateWidthDisplay()
        {
            //Yes, this is reversed, width = vertical
            lblWidth.Content = $"{Properties.Resources.OnDrop_VerticalPixels}: {_width}";
        }

        private void UpdateLayerCountDisplay()
        {
            lblLayers.Content = $"{Properties.Resources.OnDrop_Layers}: {_layerCount}";
        }

        private void UpdateTimeDisplay()
        {
            var t = TimeSpan.FromSeconds(_accumulatedSeconds);

            string answer = $"{Properties.Resources.OnDrop_PrintTime}: {t.Hours:D2}h:{t.Minutes:D2}m:{t.Seconds:D2}s";

            lblTime.Content = answer;
        }

        private float GetG1MoveTime(string line)
        {
            var cleaned = line.Replace("G1", "").Trim().Split(';')[0];
            var parts = cleaned.Split(' ');

            if (parts.Length != 2) return 0;

            var z = 0;
            var f = 1;

            if (parts[0].StartsWith("F"))
            {
                z = 1;
                f = 0;
            }

            try
            {
                var zInt = float.Parse(parts[z].Replace("Z", ""));
                var fInt = float.Parse(parts[f].Replace("F", ""));

                return (60.0f / fInt) * zInt;
            }
            catch (Exception e)
            {
                _hasErrors = true;
                return 0;
            }
        }

        private int GetG4(string line)
        {
            var cleaned = line.Replace("G4 S", "").Replace(";", "");
            try
            {
                return int.Parse(cleaned);
            }
            catch (Exception)
            {
                _hasErrors = true;
                return 0;
            }
        }

        private int GetCommentInt(string line)
        {
            var cleaned = line.Replace(";","");
            var parts = cleaned.Split(':');
            try
            {
                return int.Parse(parts[1]);
            }
            catch (Exception)
            {
                _hasErrors = true;
                return -1;
            }
        }

        private void GbFile_OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
        }

        // From https://stackoverflow.com/a/3590938
        private static byte[] BitReverseTable =
        {
            0x00, 0x80, 0x40, 0xc0, 0x20, 0xa0, 0x60, 0xe0,
            0x10, 0x90, 0x50, 0xd0, 0x30, 0xb0, 0x70, 0xf0,
            0x08, 0x88, 0x48, 0xc8, 0x28, 0xa8, 0x68, 0xe8,
            0x18, 0x98, 0x58, 0xd8, 0x38, 0xb8, 0x78, 0xf8,
            0x04, 0x84, 0x44, 0xc4, 0x24, 0xa4, 0x64, 0xe4,
            0x14, 0x94, 0x54, 0xd4, 0x34, 0xb4, 0x74, 0xf4,
            0x0c, 0x8c, 0x4c, 0xcc, 0x2c, 0xac, 0x6c, 0xec,
            0x1c, 0x9c, 0x5c, 0xdc, 0x3c, 0xbc, 0x7c, 0xfc,
            0x02, 0x82, 0x42, 0xc2, 0x22, 0xa2, 0x62, 0xe2,
            0x12, 0x92, 0x52, 0xd2, 0x32, 0xb2, 0x72, 0xf2,
            0x0a, 0x8a, 0x4a, 0xca, 0x2a, 0xaa, 0x6a, 0xea,
            0x1a, 0x9a, 0x5a, 0xda, 0x3a, 0xba, 0x7a, 0xfa,
            0x06, 0x86, 0x46, 0xc6, 0x26, 0xa6, 0x66, 0xe6,
            0x16, 0x96, 0x56, 0xd6, 0x36, 0xb6, 0x76, 0xf6,
            0x0e, 0x8e, 0x4e, 0xce, 0x2e, 0xae, 0x6e, 0xee,
            0x1e, 0x9e, 0x5e, 0xde, 0x3e, 0xbe, 0x7e, 0xfe,
            0x01, 0x81, 0x41, 0xc1, 0x21, 0xa1, 0x61, 0xe1,
            0x11, 0x91, 0x51, 0xd1, 0x31, 0xb1, 0x71, 0xf1,
            0x09, 0x89, 0x49, 0xc9, 0x29, 0xa9, 0x69, 0xe9,
            0x19, 0x99, 0x59, 0xd9, 0x39, 0xb9, 0x79, 0xf9,
            0x05, 0x85, 0x45, 0xc5, 0x25, 0xa5, 0x65, 0xe5,
            0x15, 0x95, 0x55, 0xd5, 0x35, 0xb5, 0x75, 0xf5,
            0x0d, 0x8d, 0x4d, 0xcd, 0x2d, 0xad, 0x6d, 0xed,
            0x1d, 0x9d, 0x5d, 0xdd, 0x3d, 0xbd, 0x7d, 0xfd,
            0x03, 0x83, 0x43, 0xc3, 0x23, 0xa3, 0x63, 0xe3,
            0x13, 0x93, 0x53, 0xd3, 0x33, 0xb3, 0x73, 0xf3,
            0x0b, 0x8b, 0x4b, 0xcb, 0x2b, 0xab, 0x6b, 0xeb,
            0x1b, 0x9b, 0x5b, 0xdb, 0x3b, 0xbb, 0x7b, 0xfb,
            0x07, 0x87, 0x47, 0xc7, 0x27, 0xa7, 0x67, 0xe7,
            0x17, 0x97, 0x57, 0xd7, 0x37, 0xb7, 0x77, 0xf7,
            0x0f, 0x8f, 0x4f, 0xcf, 0x2f, 0xaf, 0x6f, 0xef,
            0x1f, 0x9f, 0x5f, 0xdf, 0x3f, 0xbf, 0x7f, 0xff
        };
        private static byte ReverseWithLookupTable(byte toReverse)
        {
            return BitReverseTable[toReverse];
        }

        private void slLayer_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            GenerateBitmap((int)slLayer.Value - 1);
            lblLayer.Content = (int)slLayer.Value;
        }
    }
}
