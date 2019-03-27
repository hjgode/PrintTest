using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;
using System.IO;
using System.Threading;

namespace PrintTest
{
    public partial class Form1 : Form,IDisposable
    {
        SerialPort _serial=null;
        Thread _th;
        Thread _readTh;
        Encoding myEncoding = Encoding.GetEncoding(28591); //28591  iso-8859-1
        Encoding myEncoding437 = Encoding.GetEncoding("IBM437");
        Encoding myEncodingMacRoman = Encoding.GetEncoding("macintosh"); //10000

        String[] _test =  { "<--@<--wm    RECEIPT\r\n" + //w = set font, m = font 'number', <-- = ESC!
                            "<--w\r\n" +
                            "Item #1 - yellow version                  $1.00\r\n" +
                            "Item #2 - blue version                    $1.00\r\n" +
                            "Item #3 - red version                     $1.00\r\n" +
                            "<--wm\r\n" +
                            "TOTAL: $3.00\r\n" +
                            "<--EZ\r\n",
                            "<--{FN?}\r\n",
                            "<--@<--EZ{TP}\r\n",
                            "<--{SDV}<-- EZ{COMMIT}{LP}",
                          };
        byte[] TestBuff = { 0x1b, (byte)'E', (byte)'Z', (byte)'{', (byte)'F', (byte)'N', (byte)'?', (byte)'}', 0x0d, 0x0a };

        string asciiString;
        //string[] fontIDs = { "(", ")", "+", "\"", "m", "&", "!" };
        // pb42: 0x28, 0x29, 0x2b, 0x6d, 0x22, 0x26, 0x21, 0x42
        //         )    (      +    m      "     &    !    B
        //         OCR-A
        //              OCR-B
        //                     Monospace821 Bold 26
        //                          Monospace821 WLG4 16
        //                                 Monospace821 WLG4 24
        //                                       Roman Bold 26
        //                                            US Standard CP437/EU
        //                                                 Enhanced CNDS
        //string[] fontNames = { "OCR-A", "OCR-B", "Monospace821 Bold 26", "Monospace821 WLG4 16", "Monospace821 WLG4 24", "Roman Bold 26", "US Standard CP437/EU" };

        class myFont
        {
            public byte id;
            public char idChar;
            public string name;
            public myFont(byte i, char idC, string n)
            {
                id = i;
                idChar = idC;
                name = n;
            }
            public myFont(byte i, string n)
            {
                id = i;
                idChar = Convert.ToChar(id);
                name = n;
            }
            public override string ToString()
            {
                return name;
            }
            public byte getID()
            {
                return id;
            }
        }
        myFont[] allRomanFonts ={  
                                    new myFont(0x66, 'f', "Arabic CP1256 10x16"), 
                                    new myFont(0x46, 'F', "Arabic CP1256 14x24"), 

                                    new myFont(0x70, 'p', "Cyrillic 1251 10x16"), 
                                    new myFont(0x50, 'P', "Cyrillic 1251 14x24"), 
                                    
                                   new myFont(0x42, 'B', "Enhanced CNDS"),

                                   new myFont(0x69, 'i', "Greek CP1253 10x16"), 
                                   new myFont(0x49, 'I', "Greek CP1253 14x24"), 

                                   new myFont(0x6E, 'n', "Hebrew CP1255 10x16"), 
                                   new myFont(0x4E, 'N', "Hebrew CP1255 14x24"), 

                                   new myFont(0x28, ')', "OCR-A"), 
                                   new myFont(0x29, '(', "OCR-B"), 
                                   new myFont(0x2B, '+', "Monospace821 Bold 26"), 
                                   new myFont(0x22, 'm', "Monospace821 WLG4 16"), 
                                   new myFont(0x6d, '"', "Monospace821 WLG4 24"), 
                                   new myFont(0x26, '&', "Roman Bold 26"), 

                                   new myFont(0x6A, 'j', "Thai CP874 10x16"), 
                                   new myFont(0x4A, 'J', "Thai CP874 14x24"), 

                                   new myFont(0x21, '!', "US Standard CP437/EU"), 

                                   new myFont(0x2A, '*', "mf156 ISO-8859-1"),
                                   //new myFont(0x4C, 'L', "ZP21P CP437 12-25"),
                                   //new myFont(0x7A, 'z', "fc226_z ISO-8859-1"),
                                   new myFont(0x7A, 'z', "is204_z ISO-8859-1"),
                                   new myFont(0x5B, '[', "is340 ISO-8859-1"),
                                   //new myFont(0x41, 'A', "zp00i ISO-8859-1"),
                                   //new myFont(0x41, 'A', "zp02i_a ISO-8859-1"),
                                   //new myFont(0x44, 'D', "zp04i_a ISO-8859-1"),
                                };
        class charSet
        {
            public byte value;
            public string name;
            public charSet(byte i, string n)
            {
                value = i;
                name = n;
            }
        }
        charSet[] charSets= { new charSet(0,"USA"), new charSet(1,"France"), new charSet(2,"Germany"), new charSet(3,"UK") , 
                                new charSet(4,"Denmark"), new charSet(5,"Sweden"), new charSet(6, "Italy"), new charSet(7,"Spain") };
        string sForIntChars = "# $ @ [ \\ ] ^ ` { | } ~"; //hex 23 24 40 5B 5C 5D 5E 60 7B 7C 7D 7E

        static int _testID = 0;

        byte[] getAsciiTable(byte[] bSeparator)
        {
            List<byte> bASCII=new List<byte>();
            // bytes 32 to 255
            for (byte b = 32; b < 255; b++ )
            {
                if (b > 32)
                    bASCII.AddRange(bSeparator);
                if ( (b % 16) == 0) //add some stuff every 16 bytes
                {
                    bASCII.Add(0x0d);
                    bASCII.Add(0x0a);
                    bASCII.AddRange(myEncoding.GetBytes("0x" + b.ToString("x02")));// add row number in hex
                    bASCII.AddRange(bSeparator);
                }
                bASCII.Add(b);
            }
            return bASCII.ToArray();
        }

        string getCharSets()
        {
            string s="";

            for (int x = 0; x < allRomanFonts.Length; x++)
            { // for each font                
                s += "\r\n\x1b" + "w" + allRomanFonts[x].idChar + allRomanFonts[x].name + "\r\n";

                for (int i = 0; i < charSets.Length; i++)
                {
                    {
                        //set charset and print
                        s += "\x1b" + "R" + myEncoding.GetString(new byte[]{charSets[i].value}) + charSets[i].name + "\r\n" + sForIntChars + "\r\n";
                    }
                }
            }
            return s;
        }

        void makeAsciiString()
        {
            asciiString = "";
            for (byte c = 32; c < 128; c++)
                asciiString += myEncoding.GetString(new byte[] { c });
        }
        public Form1()
        {
            InitializeComponent();
            makeAsciiString();

            listBox1.Items.Add("Receipt");
            listBox1.Items.Add("Font list query");
            listBox1.Items.Add("Test print");
            listBox1.Items.Add("Factory Reset");
            listBox1.Items.Add("Font Test"); //4
            listBox1.Items.Add("Charset Test"); //5
            listBox1.Items.Add("ASCII table"); //6
            listBox1.Items.Add("Single ASCII table"); //6
            listBox1.SelectedIndex = 0;

            lstFonts.Items.Clear();
            lstFonts.Items.AddRange(allRomanFonts);
            lstFonts.SelectedIndex = 0;
        }

        public new void Dispose()
        {
            btnClose_Click(this, new EventArgs());
        }

        private void btnPrint_Click(object sender, EventArgs e)
        {
            try
            {

                _th = new Thread(new ThreadStart(wThread));
                _testID = listBox1.SelectedIndex;
                _th.Start();
            }
            catch (Exception ex)
            {
                addLog("Exception: " + ex.Message);
            }
        }


        void _serial_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            addLog("ERROR: " + ((SerialError)(e.EventType)).ToString());
        }

        void rThread()
        {
            try
            {
                while (true)
                {
                    string s = "";
                    while (_serial.BytesToRead > 0)
                    {
                        byte[] b = new byte[_serial.BytesToRead];
                        _serial.Read(b, 0, _serial.BytesToRead);
                        s += Encoding.GetEncoding(28591).GetString(b);
                    }
                    if(s.Length>0)
                        addLog(s);
                }
            }
            catch (Exception ex)
            {
                addLog("Exception-rThread: " + ex.Message);
            }
        }

        string getFontTestLines()
        {
            string s = "";
            int i = 0;
            for (int x = 0; x < allRomanFonts.Length; x++ )
            {
                s += "\x1b" + "w" + allRomanFonts[x].idChar + allRomanFonts[x].name + "\r\n" + asciiString + "\r\n";
            }
            return s;
        }

        void wThread(){
            try
            {
                addLog("start printing...");
                String toPrinter = "";
                if (_testID < 4)
                    toPrinter = _test[_testID].Replace("<--", "\x1B");
                else if (_testID == 4)
                {
                    toPrinter = getFontTestLines();
                }
                else if (_testID == 5)
                {
                    toPrinter = getCharSets();
                }
                else if (_testID == 6)
                {
                    for (int x = 0; x < allRomanFonts.Length; x++)
                    { // for each font                
                        toPrinter += "\r\n\x1b" + "w" + allRomanFonts[x].idChar + allRomanFonts[x].name + "\r\n";
                        toPrinter += myEncoding.GetString(getAsciiTable(new byte[]{0x09}));
                    }
                }
                else if (_testID == 7) //single ASCII chart
                {
                    myFont mFont = (myFont) getSelectedFontItem();// lstFonts.SelectedItem;
                    toPrinter += "\r\n\x1b" + "w" + mFont.idChar + mFont.name + "\r\n";
                    toPrinter += myEncoding.GetString(getAsciiTable(new byte[] { 0x09 }));
                }
                addLog(toPrinter);
                byte[] buf = myEncoding.GetBytes(toPrinter);

                dumpBytes(buf);
                _serial.Write(buf, 0, buf.Length);
                _serial.Write(new byte[] { 0x0c },0,1);
                //            dumpBytes(TestBuff);
                //            _serial.Write(TestBuff, 0, TestBuff.Length);
                //_serial.BaseStream.Write(buf, 0, buf.Length);
                //_serial.BaseStream.Flush();

                addLog("printing done...");
            }
            catch (Exception ex)
            {
                addLog("Exception-wThread: " + ex.Message);
            }
        }

        private delegate object getListItem();
        private object getSelectedFontItem()
        {
            if (lstFonts.InvokeRequired)
            {
                getListItem gci = new getListItem(getSelectedFontItem);
                return lstFonts.Invoke(gci);
            }
            else
                return lstFonts.SelectedItem;
        }

        void dumpBytes(byte[] b)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter("dumpBytes.txt"))
                {
                    sw.Write(myEncoding.GetString(b));
                    addLog(myEncoding.GetString(b));
                }
                using (BinaryWriter sw = new BinaryWriter(File.Open("Bytes.txt",FileMode.Create)))
                {
                    sw.Write(b,0,b.Length);
                    addLog("binary file written as bytes.txt");
                }
            }
            catch (Exception ex)
            {
                addLog("Exception-dumpBytes: " + ex.Message);
            }
        }

        void _serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            StringBuilder sb = new StringBuilder();
            String sIn;
            try
            {
                sIn = _serial.ReadExisting();
                addLog("<<< " + sIn);

            }
            catch (Exception ex)
            {
                addLog("Exception _serial_DataReceived: " + ex.Message);
            }
        }

        delegate void SetTextCallback(string text);
        public void addLog(string text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.txtLog.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(addLog);
                this.Invoke(d, new object[] { text });
            }
            else
            {
                if (txtLog.Text.Length > 2000)
                    txtLog.Text = "";
                txtLog.Text += text + "\r\n";
                txtLog.SelectionLength = 0;
                txtLog.SelectionStart = txtLog.Text.Length - 1;
                txtLog.ScrollToCaret();
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            if (_serial == null)
                return;
            if (_serial.IsOpen)
            {
                _serial.Close();
                _serial.Dispose();
                _serial = null;
                addLog("port closed");
            }
            if (_readTh != null)
                _readTh.Abort();
            if (_th != null)
                _th.Abort();
        }

        private void btnOpen_Click(object sender, EventArgs e)
        {
            if (_serial != null && _serial.IsOpen)
            {
                addLog("Please close port");
                return;
            }
            addLog("opening " + txtComPort.Text + " ...");
            _serial = new SerialPort(txtComPort.Text, 115200, Parity.None, 8, StopBits.One);
            _serial.Handshake = Handshake.RequestToSend;
            _serial.Encoding = Encoding.GetEncoding(28591); //28591  iso-8859-1
            _serial.Open();
            addLog("port opened");

            _serial.DataReceived += new SerialDataReceivedEventHandler(_serial_DataReceived);
            _serial.ErrorReceived += new SerialErrorReceivedEventHandler(_serial_ErrorReceived);

            //_readTh = new Thread(new ThreadStart(rThread));
            //_readTh.Start();

        }

        private void btnBinary_Click(object sender, EventArgs e)
        {
            myFont theFont = (myFont)(lstFonts.SelectedItem);
            /*
             * the default printer code page is MacRoman
             * the other one is MacRoman
             */
            if (_serial == null || !_serial.IsOpen)
            {
                addLog("Open COM first");
                return;
            }
            List<byte> buf = new List<byte>();
            //reset
            byte[] bReset = { 0x1b, 0x40 }; // esc @
            //select font
//            byte[] bSetFont = { 0x1B, 0x77, 0x2B }; //esc w plus one of 0x2b, 0x22, 0x6d, 0x21
            byte[] bSetFont = { 0x1B, 0x77, 0x21 }; //esc w with US Standard
            myFont[] fonts = { theFont };
                                   //new myFont(0x2B, '+', "Monospace821 Bold 26")};
                                   //,new myFont(0x22, 'm', "Monospace821 WLG4 16"), 
                                   //new myFont(0x6d, '"', "Monospace821 WLG4 24"), 
                                   //new myFont(0x26, '&', "Roman Bold 26"), 
                                   //new myFont(0x21, '!', "US Standard CP437/EU"), 
                                   //new myFont(0x42, 'B', "Enhanced CNDS")};
            foreach (myFont mf in fonts)
            {
                buf.Clear();
                bSetFont[2] = mf.id;
                //tab spacing
                //byte[] bSetTabPos ={ 0x1B, 0x44, 
                //                      2, 4, 6, 8, 10, 12, 14, 16, 18, 20,
                //                      22, 24, 26, 28, 30, 32, 34, 46, 48, 50,
                //                      0x00}; //esc D n1...nk 0x00
                buf.AddRange(bReset);
                buf.Add(0x0d);
                buf.Add(0x0a);
                buf.AddRange(Encoding.ASCII.GetBytes(mf.name));
                buf.AddRange(bSetFont);
                buf.Add(0x0d);
                buf.Add(0x0a);
                //buf.AddRange(bSetTabPos);

                buf.AddRange(getAsciiTable(new byte[] { 0x20 }));
                buf.Add(0x0d);
                buf.Add(0x0a);

                dumpBytes(buf.ToArray());

                _serial.Write(buf.ToArray(), 0, buf.Count);                
            }
        }
    }
}
