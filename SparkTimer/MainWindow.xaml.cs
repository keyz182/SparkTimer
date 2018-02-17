using System;
using System.IO;
using System.Windows;

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
        private int _accumulatedSeconds;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void GbFile_OnDrop(object sender, DragEventArgs e)
        {
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


            try
            {
                // Open the text file using a stream reader.
                using (var sr = new StreamReader(file))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.StartsWith("{{"))
                        {
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
                            var chars = line.ToCharArray();
                            switch (chars[0])
                            {
                                case ';':
                                {
                                    //comment
                                    if (chars[1] == 'W' || chars[1] == 'w')
                                    {
                                        _width = GetCommentInt(line);
                                        UpdateWidthDisplay();
                                    }
                                    else if (chars[1] == 'H' || chars[1] == 'h')
                                    {
                                        _height = GetCommentInt(line);
                                        UpdateHeightDisplay();
                                    }
                                    else if (chars[1] == 'L' || chars[1] == 'l')
                                    {
                                        _layerCount = GetCommentInt(line);
                                        UpdateLayerCountDisplay();
                                    }

                                    break;
                                }
                                case 'G':
                                case 'g':
                                    //G code
                                    if (chars[1] == '4'  && chars[2] == ' ')
                                    {
                                        _accumulatedSeconds += GetG4(line);
                                        //13 Seconds seems to be to be the time between layers, may be honed in over time.
                                        _accumulatedSeconds += 13;
                                        UpdateTimeDisplay();
                                    }
                                    break;
                                case 'M':
                                case 'm':
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
                Console.WriteLine(Properties.Resources.OnDrop_FileCouldNotBeRead);
                Console.WriteLine(error.Message);
            }
            finally
            {
                Console.WriteLine(Properties.Resources.OnDrop_Done);
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

        private static int GetG4(string line)
        {
            var cleaned = line.Replace("G4 S", "").Replace(";", "");
            try
            {
                return int.Parse(cleaned);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static int GetCommentInt(string line)
        {
            var cleaned = line.Replace(";","");
            var parts = cleaned.Split(':');
            try
            {
                return int.Parse(parts[1]);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private void GbFile_OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
        }
    }
}
