using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;


struct SerialBooks                       //用于储存用户选择串口属性
{
    public string comNum;
    public Int32 baudRate;
    public int startBits;
    public bool startFlag;
    public int parityBit;
    public int endBits;
    public bool endFlag;
    public int frameintervalTime;
}

struct Loop
{
    public int loopCount;
    public int loopInterval;
    public int loopLoc;
    public int loopSize;
}

struct MultiStr
{
   public bool strFlag;
   public string str;
}

struct CirclebufBook
{
    public byte head_p;
    public byte tail_p;
    public byte length;
}

struct AutodrawingParameter
{
    public bool enable;
    public float maxY;
    public float cycle;
    public int speed;
    public float calibration;
}

struct PictureParameter
{
    public int width;
    public int height;
    public int x;
    public int y;
}

namespace ImageSerport_Assistant
{
    public partial class Form1 : Form
    {

        [DllImport("kernel32.dll")]
        public static extern Boolean AllocConsole();
        [DllImport("kernel32.dll")]
        public static extern Boolean FreeConsole();

        const int CircleBuffer_Size = 128;        //定义环形缓冲大小
        const int waveformBuffer_Size = 150;

        SerialBooks serialBook = new SerialBooks();

        Loop loopVar = new Loop();
        MultiStr[] multi_Strs = new MultiStr[20]; //没能写成局部变量 遗憾

        string[] circleBuffer = new string[CircleBuffer_Size];   //单生产者(接收数据)单消费者(在窗口中显示)模型
        CirclebufBook circlebufBook = new CirclebufBook();

        //以下为示波器调用
        float[] waveformBuffer = new float[waveformBuffer_Size];    
        Point[] waveformLoc = new Point[waveformBuffer_Size];
        AutodrawingParameter autodrawing = new AutodrawingParameter();
        CirclebufBook waveformBook = new CirclebufBook();

        Stopwatch cycleWatch = new Stopwatch(); //提供波速解析服务


        //以下为串口图像调用
        Point mouseclickLoc = new Point(); 
        bool mouseclickFlag = false;
        PictureParameter imageBook = new PictureParameter();

        /// <summary>
        /// 自定义变量初始化方法
        /// </summary>
        public void IniMyControls()
        {
            comCombo.SelectedIndex = 0;         //初始化combo内容
            baudCombo.SelectedIndex = 3;
            parityCombo.SelectedIndex = 0;
            timestyleCombo.SelectedIndex = 0;

            serialBook.comNum = "NULL";         //初始化串口字典
            serialBook.baudRate = 9600;
            serialBook.startFlag = false;
            serialBook.endFlag = false;
            serialBook.frameintervalTime = -1;

            circlebufBook.tail_p = 0;    //初始化环形缓冲区字典
            circlebufBook.head_p = 0;
            circlebufBook.length = 0;

            loopVar.loopSize = -1;

            /*启用双重缓冲*/
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.DoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();

            waveformBook.tail_p = 0;
            waveformBook.length = 0;
            waveformBook.head_p = 149;

            autodrawing.speed = 1;
            autodrawing.enable = true;

            for (int i = 0; i < 150; i++)
            {
                waveformLoc[i] = new Point(i*5, 218);
            }

            imageBook.width = 240;
            imageBook.height = 320;
            imageBook.x = 0;
            imageBook.y = 0;

            myPicture.MouseWheel += new System.Windows.Forms.MouseEventHandler(myPicture_MouseWheel);
        }

        /// <summary>
        /// 将文本框内字符串处理为16进制字符串数组并发送 返回值用于添加至文本框
        /// </summary>
        /// <param name="str">从文本框中读取的内容</param>
        /// <param name="data_type">以何种方式处理( false为hex，true为字符串)</param>
        private string[] data_send(string str)
        {
            str = (str.Length % 2 == 0) ? str : ("0" + str);    //补位
            string[] strArray = new string[str.Length / 2];

            byte[] data = new byte[1];        //对数据分包处理 每帧为一个byte

            for (int i = (str.Length / 2) - 1; i >= 0 ; i--)    //强制要求以小端字序进行通讯
            {
                strArray[i] += Convert.ToInt32(str.Substring(i * 2, 2),16); //分割字符串
                data[0] = Convert.ToByte(strArray[i]);
                serialPort1.Write(data, 0, 1);           //发送
            }

            return strArray;
        }

        /// <summary>
        /// 添加串口数据标识（time? , In/Out）该方法不应调用，已封装
        /// </summary>
        /// <param name="sendFlag">
        public string get_identifier(bool sendFlag)
        {
            string identifier = null;

            if (timestampeEnable.Checked)   //时间戳处理
            {
                if (timestyleCombo.SelectedIndex == 0)
                {
                    identifier = DateTime.Now.ToString("hh:mm:ss.ffff");
                }
                else
                {
                    identifier = DateTime.Now.ToString();
                }
            }

            if (sendFlag)
                identifier += " ==> ";
            else
                identifier += " <== ";

            return identifier;
        }

        /// <summary>
        /// 向对话框写入刚刚发出的数据（发送的内容）
        /// </summary>
        /// <param name="str">要添加的内容</param>
        /// <param name="data_type">以何种方式处理( false为hex，true为字符串)</param>
        private void talking_Add(string str, bool data_type)
        {
            if (tabControl.SelectedIndex != 0) return;
            if(timer1.Enabled)
                talkingDialog.AppendText("\r\n");

            unchecked       //修改颜色 绿色表示发送出的数据
            {
                talkingDialog.SelectionColor = Color.FromArgb((int)0xFF00CC00);
            }

            talkingDialog.AppendText(get_identifier(true));
            talkingDialog.SelectionColor = Color.Black;

            if (!data_type)
            {
                str = (str.Length % 2 == 0) ? str : ("0" + str);    //补位
                for (int i = 0; i < str.Length / 2; i++)
                {
                    talkingDialog.AppendText(str.Substring(i * 2, 2).ToUpper() + "h ");//添加发送内容
                }
            }
            else
                talkingDialog.AppendText(str);

            talkingDialog.AppendText("\r\n");
        }

        /// <summary>
        /// 向对话框写入环形缓冲区内容(接收到的内容)
        /// </summary>
        /// <param name="sendFlag">
        public void writr_talkingDialog()
        {
            if (tabControl.SelectedIndex != 0) return;

            talkingDialog.SelectionColor = Color.Orange;
            talkingDialog.AppendText(get_identifier(false));
            talkingDialog.SelectionColor = Color.Black;

            talkingDialog.VScroll -= new System.EventHandler(talkingDialog_VScroll);    //取消订阅绘图服务，节省运算资源
            talkingDialog.TextChanged -= new System.EventHandler(talkingDialog_TextChanged);

            while (circlebufBook.length != 0)
            {
                talkingDialog.AppendText(circleBuffer[circlebufBook.head_p]);

                if (++circlebufBook.head_p == CircleBuffer_Size) circlebufBook.head_p = 0;


                if (circlebufBook.length != 0)
                    circlebufBook.length--;
            }

            talkingDialog.VScroll += new System.EventHandler(talkingDialog_VScroll);
            talkingDialog.TextChanged += new System.EventHandler(talkingDialog_TextChanged);

            if (serialBook.frameintervalTime == -1)     //未启用帧间隔服务
                talkingDialog.AppendText("\r\n");
            else if (timer1.Enabled) //帧间隔服务已启用，时间未到
            {
                timer1.Stop();
                timer1.Start();
            }
            else timer1.Start(); //帧间隔服务已启用，时间已到
        }

        /// <summary>
        /// 从串口读数据 返回byte数组
        /// </summary>
        public byte[] get_serdata()
        {
            byte[] receivedData = new Byte[serialPort1.BytesToRead];
            serialPort1.Read(receivedData, 0, receivedData.Length);
            //byte data1 = (byte)serialPort1.ReadByte(); //太慢

            return receivedData;
        }


        public Form1()
        {
            InitializeComponent();
            IniMyControls();
            ComboBox.CheckForIllegalCrossThreadCalls = false;
        }

        /*///////////////////////////////////////////文本模式&基础交互/////////////////////////////////////////////////////*/

        /*————————————————按钮服务———————————————————*/

        private void bufferclearBtn_Click(object sender, EventArgs e)   //缓冲清除
        {
            serialPort1.DiscardInBuffer();
            serialPort1.DiscardOutBuffer();
            circlebufBook.tail_p = 0;
            circlebufBook.length = 0;
            circlebufBook.head_p = 0;
        }

        private void clktalkBtn_Click(object sender, EventArgs e)   //清除对话区
        {
            talkingDialog.Clear();
        }

        private void saveBtn_Click(object sender, EventArgs e)  //将talkingDialog内容以数据流写入
        {
            if (talkingDialog.Text == "")
            {
                if (DialogResult.OK == saveFileDialog1.ShowDialog())
                {
                    var filePath = saveFileDialog1.FileName;

                    using (StreamWriter sw = new StreamWriter(filePath))
                    {
                        sw.WriteLineAsync(talkingDialog.Text);
                        sw.Dispose();
                    }
                }
            }
            else
            {
                MessageBox.Show("啥都木有啊,你想保存点啥 ψ(._. )>");
            }
        }

        private void stopsendBtn_Click(object sender, EventArgs e) //循环发送停止按钮
        {
            timer2.Stop();
            serportBtn.Enabled = true;
            loopintervalText.Enabled = true;
            looptimeText.Enabled = true;
            startloopBtn.Enabled = true;
            stopsendBtn.Enabled = false;
        }

        private void loopsendBtn_Click(object sender, EventArgs e)  //循环发送按钮（循环参数处理）
        {
            if (!serialPort1.IsOpen) //入口检测
            {
                MessageBox.Show("请先打开串口 ...(*￣０￣)ノ");
                return;
            }

            for (int i = 0; i < 20; i++)    //遍历寻找非空的最大发送长度
            {
                if (multi_Strs[i].str == "" | multi_Strs[i].str == null)
                {
                    loopVar.loopSize = i;
                    if (i == 0) loopVar.loopSize = -1;
                    break;
                }

            }

            if (loopVar.loopSize == -1)
            {
                MessageBox.Show("无发送内容或存在格式错误");
                return;
            }

            try
            {
                loopVar.loopInterval = Convert.ToInt32(loopintervalText.Text);
                loopVar.loopCount = Convert.ToInt32(looptimeText.Text);
            }
            catch (Exception ex)
            {
                loopVar.loopInterval = 0;
                loopVar.loopCount = -1;
                MessageBox.Show(ex.Message);
                return;
            }

            if (loopVar.loopCount == 0)
            {
                MessageBox.Show("循环次数不可为零，负数为无限次发送");
                return;
            }
            else
            {
                loopVar.loopCount = loopVar.loopCount < 0 ? -1 : loopVar.loopCount; //负数统一显示为-1
                looptimeText.Text = loopVar.loopCount.ToString();
            }

            if (loopVar.loopInterval > 0)
            {
                timer2.Interval = loopVar.loopInterval;
                timer2.Start();
                serportBtn.Enabled = false;
                serportBtn.Enabled = false;
                loopintervalText.Enabled = false;
                looptimeText.Enabled = false;
                startloopBtn.Enabled = false;
                stopsendBtn.Enabled = true;
            }
            else
            {
                MessageBox.Show("发送间隔不得小于等于0");
            }

        }

        private void sendbaseBtn_Click(object sender, EventArgs e)  //发送按钮
        {

            if (!serialPort1.IsOpen) //入口检测
            {
                MessageBox.Show("请先打开串口 ...(*￣０￣)ノ");
                return;
            }
            if (!((inputText0.Text != "" && ((sender as Button).Name) == "sendbaseBtn0") || (inputText1.Text != "" && ((sender as Button).Name) == "sendbaseBtn1")))
            {
                ; MessageBox.Show("请输入数据");
                return;
            }

            string str = null;
            switch ((sender as Button).Name)    //捕获数据
            {
                case "sendbaseBtn0":
                    str = inputText0.Text;
                    break;

                case "sendbaseBtn1":
                    str = inputText1.Text;
                    break;
                default: break;
            }

            string[] vs = { str, (sender as Button).Tag.ToString() };
            if (!sendWorker.IsBusy)   //异步发送    
            {
                sendWorker.RunWorkerAsync(vs);
            }

            //添加内容
            talking_Add(str, (sender as Button).Tag.ToString() == "2");
        }

        private void sendBtn_Click(object sender, EventArgs e)     //提供多字符串中的单字符发送按钮
        {
            int tag = Convert.ToInt32(((sender as Button).Tag.ToString()));

            try
            {
                if (!multi_Strs[tag].strFlag)
                {
                    data_send(multi_Strs[tag].str);
                    talking_Add(multi_Strs[tag].str, multi_Strs[tag].strFlag);
                }
                else
                {
                    serialPort1.Write(multi_Strs[tag].str);
                    talking_Add(multi_Strs[tag].str, !multi_Strs[tag].strFlag);
                }
            }
            catch (Exception ex)
            {
                loopVar.loopSize = -1;  //存在错误不可发送
                MessageBox.Show(ex.ToString());
            }

        }

        /*————————————————串口属性设置服务———————————————————*/

        private void ComCombo_DropDown(object sender, EventArgs e)  //在下拉列表时获取可用串口
        {
            comCombo.Items.Clear();

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_PnPEntity where Name like '%(COM%'"))
            {
                var hardInfos = searcher.Get();
                foreach (var hardInfo in hardInfos)
                {
                    if (hardInfo.Properties["Name"].Value != null)
                    {
                        string deviceName = hardInfo.Properties["Name"].Value.ToString();
                        comCombo.Items.Add(deviceName);
                    }
                }
                if (comCombo.Items == null)
                {
                    comCombo.Items.Add("NULL");
                    comCombo.SelectedIndex = 0;
                }
                AdjustComboBoxDropDownListWidth((ComboBox)sender);
                searcher.Dispose();
            }
        }

        private void serportBtn_Click(object sender, EventArgs e)   //串口开关
        {
            if (comCombo.Text != "NULL")
            {
                try                             //主逻辑结构
                {
                    if (serialPort1.IsOpen)
                    {
                        serialPort1.Close();
                        comCombo.Enabled = true;
                        serportBtn.Text = "打 开 串 口";
                        serportBtn.BackColor = Color.Red;
                        bufferclearBtn.Enabled = false;
                        inputText0.Enabled = false;
                        inputText1.Enabled = false;
                        sendbaseBtn0.Enabled = false;
                        sendbaseBtn1.Enabled = false;

                        foreach (Control control in loopsendPanel.Controls) 
                        {
                            if (control is Button)
                                ((Button)control).Enabled = false;
                        }
                    }
                    else
                    {
                        serialPort1.Open();
                        comCombo.Enabled = false;
                        serportBtn.Text = "关 闭 串 口";
                        serportBtn.BackColor = Color.Lime;
                        bufferclearBtn.Enabled = true;
                        inputText0.Enabled = true;
                        inputText1.Enabled = true;
                        sendbaseBtn0.Enabled = true;
                        sendbaseBtn1.Enabled = true;

                        foreach (Control control in loopsendPanel.Controls)
                        {
                            if (control is Button)
                                ((Button)control).Enabled = true;
                        }
                    }
                }
                catch (Exception err)
                {
                    MessageBox.Show(err.Message);
                }
            }
            else
            {
                MessageBox.Show("请选择参数!!!!");
            }
        }

        private void serport_comboUpdata(object sender, EventArgs e)//更新combo类串口设置
        {
            if (((System.Windows.Forms.Control)sender).Text == "NULL" | ((System.Windows.Forms.Control)sender).Text == "") return;

            switch (((System.Windows.Forms.Control)sender).Name)
            {
                case "comCombo":
                    serialBook.comNum = comCombo.Text.Substring(comCombo.Text.IndexOf("COM"), 4);
                    serialPort1.PortName = serialBook.comNum;
                    break;

                case "baudCombo":
                    serialBook.baudRate = Convert.ToInt32(baudCombo.Text);
                    serialPort1.BaudRate = serialBook.baudRate;

                    if (serialBook.frameintervalTime != -1)         //重新计算帧间隔时间
                    {
                        serialBook.frameintervalTime = (int)(Convert.ToInt32(timeintervalText.Text) * (8000 / (float)Convert.ToInt32(baudCombo.Text)));
                        MessageBox.Show("间隔时间被设为:" + serialBook.frameintervalTime.ToString() + "(如不大于0将会禁用)");
                        if (serialBook.frameintervalTime <= 0)
                        {
                            serialBook.frameintervalTime = -1;
                            timeintervalText.Text = "";
                        }

                    }
                    break;

                case "parityCombo":
                    serialBook.parityBit = parityCombo.SelectedIndex;
                    serialPort1.Parity = (System.IO.Ports.Parity)serialBook.parityBit;
                    break;

                default: break;
            }
        }

        private void serport_setStyle(object sender, EventArgs e)   //修改界面风格
        {
            if (((System.Windows.Forms.Control)sender).Text != "NULL" && ((System.Windows.Forms.Control)sender).Text != "")
            {
                ((System.Windows.Forms.Control)sender).ForeColor = Color.Black;
            }
            else
            {
                ((System.Windows.Forms.Control)sender).ForeColor = Color.Gray;
                ((System.Windows.Forms.Control)sender).Text = "NULL";
            }
        }

        private void serport_textUpdata(object sender, KeyPressEventArgs e) //更新Text类串口设置
        {
            if (e.KeyChar == Convert.ToChar(13))
            {
                if (((System.Windows.Forms.Control)sender).Text != "NULL" && ((System.Windows.Forms.Control)sender).Text != "")
                {
                    try
                    {
                        switch (((System.Windows.Forms.Control)sender).Name)
                        {
                            case "startText":
                                serialBook.startBits = Convert.ToInt32(startText.Text, 16);
                                serialBook.startFlag = true;
                                break;

                            case "endText":
                                serialBook.endBits = Convert.ToInt32(endText.Text, 16);
                                serialBook.endFlag = true;
                                break;

                            case "timeintervalText":
                                serialBook.frameintervalTime = (int)(Convert.ToInt32(timeintervalText.Text) * (8000 / (float)Convert.ToInt32(baudCombo.Text)));
                                MessageBox.Show("间隔时间被设为:" + serialBook.frameintervalTime.ToString() + " ms (如不大于0将会禁用)");
                                if (serialBook.frameintervalTime <= 0)
                                {
                                    if (timer1.Enabled) timer1.Stop();
                                    serialBook.frameintervalTime = -1;
                                    timeintervalText.Text = "";
                                }
                                else
                                {
                                    timer1.Interval = serialBook.frameintervalTime;
                                }
                                break;

                            default: break;
                        }
                        ActiveControl = null;
                    }
                    catch
                    {
                        MessageBox.Show("请输入合法字符！！！");
                    }
                }
                else
                {
                    switch (((System.Windows.Forms.Control)sender).Name)
                    {
                        case "startText":
                            serialBook.startBits = 0;
                            serialBook.startFlag = false;
                            break;

                        case "endText":
                            serialBook.endBits = 0;
                            serialBook.endFlag = false;
                            break;

                        case "frametimeText":
                            serialBook.frameintervalTime = -1;
                            if (timer1.Enabled) timer1.Stop();
                            break;

                        default: break;
                    }
                    ActiveControl = null;
                }
                e.Handled = true;   //消除警报音
            }
        }

        private void timestampeEnable_CheckedChanged(object sender, EventArgs e) //时间戳界面服务    
        {
            timestyleCombo.Enabled = timestampeEnable.Checked;
        }

        private void styleRadio_CheckedChanged(object sender, EventArgs e)  //修改收发数据模式
        {
            if (!(sender as RadioButton).Checked)
            {
                return;
            }
            switch (((System.Windows.Forms.Control)sender).Name)
            {
                case "sendstyleRadio0":
                    sendbaseBtn0.Tag = 0;
                    sendbaseBtn1.Tag = 0;
                    break;

                case "sendstyleRadio1":
                    sendbaseBtn0.Tag = 1;
                    sendbaseBtn1.Tag = 1;
                    break;

                case "sendstyleRadio2":
                    sendbaseBtn0.Tag = 2;
                    sendbaseBtn1.Tag = 2;
                    break;

                case "receiveBtn0":
                    talkingDialog.Tag = 0;
                    break;

                case "receiveBtn1":
                    talkingDialog.Tag = 1;
                    break;

                case "receiveBtn2":
                    talkingDialog.Tag = 2;
                    break;

                default:
                    break;
            }
        }

        private void test_Click(object sender, EventArgs e)
        {

            AllocConsole();
            for(int i = 0; i < waveformBuffer_Size; i++)
            {
                if(waveformBuffer[i] != 0)
                    Console.WriteLine("{0},{1}",i, waveformBuffer[i]);
                if (waveformBuffer[i] != 0)
                    Console.WriteLine("{0},{1}", i, waveformLoc[i]);
            }
            Console.WriteLine("Hallo World!");

        }

        /*———————————————————绘图服务——————————————————————*/

        private void lineNum_Show() //绘制行号方法
        {
            /*获得信息*/
            Point findLoc = talkingDialog.Location;     //定义搜索坐标
            int firstlineIndex = talkingDialog.GetCharIndexFromPosition(findLoc);   //从搜索坐标返回索引
            //搜索文本框第一行信息
            int firstlineNum = talkingDialog.GetLineFromCharIndex(firstlineIndex);          //利用索引返回行号
            Point firstlineLoc = talkingDialog.GetPositionFromCharIndex(firstlineIndex);    //利用索引返回坐标

            //更新搜索坐标
            findLoc.Y = findLoc.Y + talkingDialog.Height;   //25是底部滚动条宽度           
            int lastlineNum = talkingDialog.GetCharIndexFromPosition(findLoc);
            //搜索文本框最后一行信息
            int lastlineLoc = talkingDialog.GetLineFromCharIndex(lastlineNum);
            Point crntLastPos = talkingDialog.GetPositionFromCharIndex(lastlineNum);
            //定义变量分配行高
            int lineHeight = (firstlineNum == lastlineLoc) ? Convert.ToInt32(talkingDialog.Font.Size) : (crntLastPos.Y - firstlineLoc.Y) / (lastlineLoc - firstlineNum);

            /*生成对象*/
            Graphics g = linenumPanel.CreateGraphics(); //创造绘图环境
            Font font = new Font(talkingDialog.Font, talkingDialog.Font.Style);//获取对话框文本风格
            SolidBrush brush = new SolidBrush(linenumPanel.BackColor);  //笔刷对象实例化

            /*绘图*/
            g.FillRectangle(brush, 0, 0, linenumPanel.ClientRectangle.Width, linenumPanel.ClientRectangle.Height);  //清空画布           

            brush.Color = Color.Black;//设置画笔颜色
            //绘图位置
            int brushX = linenumPanel.ClientRectangle.Width - Convert.ToInt32(font.Size * 3) + 5;
            int brushY = crntLastPos.Y + Convert.ToInt32(font.Size * 0.21f);

            for (int i = lastlineLoc; i >= firstlineNum; i--)
            {
                g.DrawString((i + 1).ToString("D3"), font, brush, brushX, brushY);
                brushY -= lineHeight;
            }

            /*注销对象*/
            g.Dispose();
            font.Dispose();
            brush.Dispose();
        }

        private void talkingDialog_TextChanged(object sender, EventArgs e)  //talkingDiolog交互
        {
            lineNum_Show();

            talkingDialog.SelectionStart = talkingDialog.TextLength;    //保证richTextbox置底
            talkingDialog.ScrollToCaret();
        }
        private void talkingDialog_VScroll(object sender, EventArgs e)
        {
            lineNum_Show();
        }

        private void AdjustComboBoxDropDownListWidth(ComboBox sender)   //提供下拉框宽度自适应服务
        {
            try
            {
                int width = sender.Width;
                Graphics g = sender.CreateGraphics();

                foreach (object s in sender.Items)
                {
                    if (s != null)  //如不为空则遍历全部获取最大长度
                    {
                        int newWidth = (int)g.MeasureString(s.ToString().Trim(), sender.Font).Width;
                        if (width < newWidth)
                            width = newWidth;
                    }
                }

                sender.DropDownWidth = width;
                g.Dispose();
            }
            catch
            { }
        }


        /*———————————————————Timer服务——————————————————————*/
        private void timer1_Tick(object sender, EventArgs e)    //提供帧间隔服务
        {
            timer1.Stop();
            talkingDialog.AppendText("\r\n");
        }

        private void timer2_Tick(object sender, EventArgs e)    //提供定时发送服务
        {
            string[] vs = { multi_Strs[loopVar.loopLoc].str, multi_Strs[loopVar.loopLoc].strFlag ? "2" : "0" };
            if (!backgroundWorker.IsBusy)   //异步发送    
            {
                sendWorker.RunWorkerAsync(vs);
            }

            talking_Add(multi_Strs[loopVar.loopLoc].str, multi_Strs[loopVar.loopLoc].strFlag);

            loopVar.loopLoc = (loopVar.loopLoc < (loopVar.loopSize -1)) ? loopVar.loopLoc + 1 : -1;

            if (loopVar.loopLoc == -1)
            {
                loopVar.loopLoc = 0;
                if(loopVar.loopCount != -1)
                {
                    loopVar.loopCount--;
                    looptimeText.Text = loopVar.loopCount.ToString();
                    if (loopVar.loopCount == 0)
                    {
                        timer2.Stop();
                        serportBtn.Enabled = false;
                        serportBtn.Enabled = false;
                        loopintervalText.Enabled = true;
                        looptimeText.Enabled = true;
                        startloopBtn.Enabled = true;
                        stopsendBtn.Enabled = false;
                    }
                }
            }
        }


        /*———————————————————线程并发服务——————————————————————*/

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e) //异步发送
        {
            string[] str = (string[])e.Argument;
            try
            {
                if (str[1] != "2")
                    data_send(str[0]);
                else
                    serialPort1.Write(str[0]);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void serialPort1_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e) //异步接收串口数据
        {
            byte[] inBuffer = get_serdata();

            //Func<bool, string> func1 = new Func<bool, string>(get_identifier);
            //Func<string> func2 = new Func<string>(get_serdata);

            switch (tabControl.SelectedIndex)
            {
                case 0:
                    if (talkingDialog.Tag.ToString() != "2")
                    {
                        for (int i = 0; i < inBuffer.Length; i++)
                        {
                            circleBuffer[circlebufBook.tail_p] = inBuffer[i].ToString("X2") + "h ";

                            if (circlebufBook.length == CircleBuffer_Size)
                            {
                                Console.WriteLine("Warning!!!!! Buffer overflow!!!");
                                break;
                            }
                            else
                            {
                                if (++circlebufBook.tail_p == CircleBuffer_Size) circlebufBook.tail_p = 0;
                                circlebufBook.length++;
                            }
                        }
                    }
                    else
                    {
                        circleBuffer[circlebufBook.tail_p] = Encoding.ASCII.GetString(inBuffer);
                        if (++circlebufBook.tail_p == CircleBuffer_Size) circlebufBook.tail_p = 0;
                        circlebufBook.length++;
                    }

                    if (backgroundWorker.IsBusy != true)
                    {
                        backgroundWorker.RunWorkerAsync();
                    }

                    break;
                case 1:     //示波模式
                            //获取下位机发送数据的周期

                    if (cycleWatch.IsRunning)
                    {
                        autodrawing.cycle = ((cycleWatch.ElapsedMilliseconds + autodrawing.cycle) / 2f) - 10f;
                        cycleWatch.Reset();
                    }
                    cycleWatch.Start();

                    //强制要求数据以int32在设备间传递 上位机接收到后再次解算为float
                    waveformBuffer[waveformBook.head_p] = BitConverter.ToInt32(inBuffer, 0);
                    //if (++waveformBook.head_p == waveformBuffer_Size) waveformBook.head_p = 0;
                    if (waveformCaculator.IsBusy != true)
                    {
                        waveformCaculator.RunWorkerAsync();
                    }

                    break;
                case 2:
                    break;
            }


        }

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)  //委托写入talkingDiolog
        {
            Action action = writr_talkingDialog;
            talkingDialog.Invoke(action);
        }


        /*———————————————————不知道咋归类的——————————————————————*/

        private void sendList_Leave(object sender, EventArgs e)     //提供定时发送服务
        {
            int tag;
            if(sender is TextBox)
            tag = Convert.ToInt32((sender as TextBox).Tag.ToString()); //捕获事件触发对象信息
            else
            tag = Convert.ToInt32((sender as CheckBox).Tag.ToString()); //捕获事件触发对象信息

            if (sender is CheckBox & multi_Strs[tag].str == null) return;    //未输入内容 不处理

            string text = "";  // <--- 要处理的信息  

            foreach (Control control in loopsendPanel.Controls) //遍历TextBox获取该发送内容（以Tag为准）
            {
                if (control is TextBox)
                    if (((TextBox)control).Name.Substring(8,1) == tag.ToString())
                        text = ((TextBox)control).Text;
            }

            if (text == "") return;
            multi_Strs[tag].str = text;
            foreach (Control control in loopsendPanel.Controls) //遍历Checkbox获取该发送内容格式（以Tag为准）
            {
                if (control is CheckBox)
                    if (((CheckBox)control).Name.Substring(8, 1) == tag.ToString())
                        multi_Strs[tag].strFlag = ((CheckBox)control).Checked;  
            }
        }

        /*///////////////////////////////////////////示波模式/////////////////////////////////////////////////////*/

        private void waveform_Refresh(object sender, PaintEventArgs e) //波形图绘制
        {
            //预准备
            Graphics g = e.Graphics;    //极其重要，下述方法会使双缓冲失效
            //Graphics g = this.CreateGraphics(); 
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;//启用抗锯齿
            Pen blackPen1 = new Pen(Color.Black);
            Pen blackPen2 = new Pen(Color.Black);
            Pen greenPen = new Pen(Color.Green);
            Pen bluePen = new Pen(Color.FromArgb(0XFF, 0X66, 0XCC, 0XFF));
            SolidBrush brush = new SolidBrush(Color.DarkBlue);

            //坐标轴绘制
            for (int i = 0; i < 9; i++)
            {
                g.DrawLine(bluePen, new Point(0, 18 + i * 50), new Point(waveformPicture.Width, 18 + i * 50));
            }
            for (int i = 1; i < 15; i++)
            {
                g.DrawLine(bluePen, new Point(i * 50, 0), new Point(i * 50, waveformPicture.Height));
                g.DrawString(( 500*i / autodrawing.speed).ToString(), DefaultFont, brush , (i*50) + 1, 219);
            }

            //边框绘制
            g.DrawLine(blackPen2, new Point(4, 0), new Point(4, waveformPicture.Height));
            g.DrawLine(blackPen2, new Point(0, 0), new Point(waveformPicture.Width, 0));
            g.DrawLine(blackPen2, new Point(waveformPicture.Width-1, 0), new Point(waveformPicture.Width-1, waveformPicture.Height));
            g.DrawLine(blackPen2, new Point(0, waveformPicture.Height-1), new Point(waveformPicture.Width, waveformPicture.Height-1));

            //波形绘制
            waveformBook.length = 0;
            for(int i = 0; i < 149; i++)
            {
                int drawingLoc0 = waveformBook.tail_p + i;
                if (drawingLoc0 > waveformBuffer_Size - 2) drawingLoc0 -= (waveformBuffer_Size - 1);
                int drawingLoc1 = drawingLoc0 + 1;
                if (drawingLoc1 == waveformBuffer_Size - 1) drawingLoc1 = 0;
                if (Math.Abs(waveformLoc[drawingLoc0].X - waveformLoc[drawingLoc1].X) < 500) 
                    g.DrawLine(greenPen, waveformLoc[drawingLoc0], waveformLoc[drawingLoc1]);                
;
            }

            //鼠标定位绘制
            Point mousePoint = waveformPicture.PointToClient(MousePosition);
            bool x = (mousePoint.X > 0 & mousePoint.X < waveformPicture.Width);
            bool y = (mousePoint.Y > 0 & mousePoint.Y< waveformPicture.Height);
            if(x && y)
            {
                g.DrawLine(blackPen1, new Point(0, mousePoint.Y), new Point(waveformPicture.Width, mousePoint.Y));
                g.DrawLine(blackPen1, new Point(mousePoint.X, 0), new Point(mousePoint.X, waveformPicture.Height));
                yvalueLable.Text = "Y: " + Math.Round((218 - mousePoint.Y) * autodrawing.calibration, 3);
            }

        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)   //用于分配串口接收的数据长度
        {
            switch(tabControl.SelectedIndex)
            {
                case 0:     //文本模式
                    refreshingTimer.Stop();
                    serialPort1.ReceivedBytesThreshold = 1;
                    break;
                case 1:     //示波模式
                    refreshingTimer.Start();
                    serialPort1.ReceivedBytesThreshold = 4;
                    break;
                case 2:     //图像模式
                    refreshingTimer.Stop();
                    break;

            }

        }

        private void refreshingTimer_Tick(object sender, EventArgs e)   //刷新波形 100HZ
        {
            //坐标平移
            if (autodrawing.enable)
            {
                for (int i = 0; i < waveformBuffer_Size; i++)
                {
                    waveformLoc[i].X -= autodrawing.speed;
                    if (waveformLoc[i].X < 0)
                    {
                        waveformBuffer[i] = 0;
                        waveformLoc[i].X += waveformPicture.Width;

                        if (++waveformBook.tail_p == waveformBuffer_Size) waveformBook.tail_p = 0;
                        if (++waveformBook.head_p == waveformBuffer_Size) waveformBook.head_p = 0;

                        if (waveformCaculator.IsBusy != true)
                        {
                            waveformCaculator.RunWorkerAsync();
                        }
                    }
                }
            }
            //数据输出
            maxLable.Text = "Max: " + Math.Round(autodrawing.maxY,3);
            frequencyLable.Text = "Cyc: " + Math.Round(autodrawing.cycle, 3);

            waveformPicture.Refresh();
        }

        private void waveparCaculate(object sender, DoWorkEventArgs e)  //波形参数计算
        {
            if (!autodrawing.enable) return;

            //这里放数据解算的方法
            float sum = 0;
            float max = 0;

            foreach(var item in waveformBuffer) //每次数据更新都获取有效数据内的最大值、平均值 反正电脑快
            {
                sum = item + sum;
                max = Math.Max(max, item);
            }

            if ( (max > autodrawing.maxY) | (max < 0.4f * autodrawing.maxY)) //数据溢出范围时重新确定最大刻度
                autodrawing.maxY = max ;


            autodrawing.calibration = autodrawing.maxY / 200f;
            averageLable.Text = "Ave: " + Math.Round(sum / waveformBuffer_Size, 3); //平均值只能在这输出....

            //时间轴移速自适应方法：环形解算（我自己想出来的方法，如有不足欢迎讨论与改进）
            //自动解算速度原理：定义单个数据的生存空间为（显示区域宽度像素/缓存区数据量）
            //应满足公式： 像素位移速度S / 像素生存空间L = ( 数据刷新率F_d / 绘图区刷新率F_c ) * 常数C
            //当利用此公式计算出的像素位移速度s0 满足（s0 / L）> 1 时，将产生起始数据突变,因此硬性规定 s满足 1 < s < L
            //常数C用来矫正反比例函数带来的阈值放大
            autodrawing.speed = (int) ( 50f / autodrawing.cycle );
            if (autodrawing.speed > 5)
            {
                autodrawing.speed = 5;
                waring1Lable.Visible = true;
            }
            if (autodrawing.speed < 1)
            {
                autodrawing.speed = 1;
                waring1Lable.Visible = false;
            }

            //坐标转换
            if(autodrawing.maxY != 0)
            {
                for (int i = 0; i < waveformBuffer_Size; i++)
                {
                    waveformLoc[i].Y = 218 - (int)(waveformBuffer[i] / (autodrawing.maxY / 200f));
                }
            }
            else
            {
                for (int i = 0; i < waveformBuffer_Size; i++)
                {
                    waveformLoc[i].Y = 218;
                }
                Console.WriteLine("No Data");
            }
            
            /////////////////滤波算法在此处编写//////////////////

        }

        private void drawingSwitch_Click(object sender, EventArgs e)
        {
            autodrawing.enable = !autodrawing.enable;
            if(cycleWatch.IsRunning) cycleWatch.Reset();
           
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                serialPort1.Dispose();
            }
            catch { }
        }

        /*///////////////////////////////////////////图像模式/////////////////////////////////////////////////////*/

        private void myPicture_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
        {            
            int widthSplit = imageBook.width / 20;
            int weightSplit = imageBook.height / 20;
            //调整缩放比例时注意与原图的比例关系
            if (e.Delta > 0)                            //如果向上滚动
            {
                myPicture.Width += weightSplit;
                myPicture.Height += widthSplit;
                myPicture.Left -= weightSplit >> 1;
                myPicture.Top -= widthSplit >> 1;
            }
            else                                        //如果向下滚动
            {
                if (myPicture.Width > weightSplit)
                {
                    myPicture.Width -= weightSplit;
                    myPicture.Height -= widthSplit;
                    myPicture.Left += weightSplit >> 1;
                    myPicture.Top += widthSplit >> 1;
                }
            }
            double temp = (double)myPicture.Width / imageBook.width;
            zoomLable.Text = "X" + temp.ToString("0.00");
        }

        private void myPicture_MouseDown(object sender, MouseEventArgs e)
        {
            mouseclickFlag = true;
            mouseclickLoc.X = e.X;
            mouseclickLoc.Y = e.Y;
        }

        private void myPicture_MouseUp(object sender, MouseEventArgs e)
        {
            mouseclickFlag = false;
        }

        private void myPicture_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseclickFlag)
            {
                myPicture.Left += Convert.ToInt16(e.X - mouseclickLoc.X);
                myPicture.Top += Convert.ToInt16(e.Y - mouseclickLoc.Y);
            }
        }
    }

}
