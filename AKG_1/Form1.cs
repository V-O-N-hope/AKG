﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Windows.Forms;

namespace AKG_1
{
    public partial class Form1 : Form
    {
        private static Size _size;
        private static string _modelPath = "E:/bolt.obj";
        private static bool _shouldDraw;
        private static Bitmap _bitmap;

        private static Vector2 _moving = Vector2.Zero;
        private static readonly Vector2 MovingX = new Vector2(10, 0);
        private static readonly Vector2 MovingY = new Vector2(0, 10);

        private static Vector4[] _vArr;

        //grid normales from file
        private static Vector3[] _vnArr;
        private static Vector3[] _updateVnArr;
        private static int[][] _vnToFArr;

        private static Vector4[] _modelVArr;

        private static Vector4[] _updateVArr;
        private static int[][] _fArr;

        private static float[][] _zBuffer;

        public Form1()
        {
            InitializeComponent();
            _shouldDraw = false;
            MakeResizing(this);

            ValuesChanger form2 = new ValuesChanger();
            form2.Show();
        }

        private static void RemakeZBuffer()
        {
            _zBuffer = new float[_size.Width][];
            for (var i = 0; i < _size.Width; i++)
            {
                _zBuffer[i] = new float[_size.Height];
            }
        }

        private static void CleanZBuffer()
        {
            for (var i = 0; i < _size.Width; i++)
            {
                for (var j = 0; j < _size.Height; j++)
                {
                    _zBuffer[i][j] = 10000.0f;
                }
            }
        }

        private static void MakeResizing(Form1 form1)
        {
            Service.CameraViewSize = _size = form1.ClientSize;
            Service.CameraView = Service.CameraViewSize.Width / (float)Service.CameraViewSize.Height;
            form1.lbHeight.Text = _size.Height.ToString();
            form1.lbWidth.Text = _size.Width.ToString();
            form1.pictureBox1.Size = _size;
            _bitmap = new Bitmap(_size.Width, _size.Height);
            RemakeZBuffer();

            if (_shouldDraw)
            {
                form1.pictureBox1.Invalidate();
            }
        }

        private void Form1_SizeChanged(object sender, EventArgs e)
        {
            MakeResizing(this);
        }

        private void miLoadModel_Click(object sender, EventArgs e)
        {
            if (fdOpenModel.ShowDialog() == DialogResult.OK)
            {
                _shouldDraw = false;
                _modelPath = fdOpenModel.FileName;
                if (File.Exists(_modelPath))
                {
                    _shouldDraw = true;
                    ObjParser parser = new ObjParser(_modelPath);
                    Service.UpdateMatrix();
                    _vArr = parser.VList.ToArray();
                    _vnArr = parser.VNList.ToArray();
                    _updateVArr = new Vector4[_vArr.Length];
                    _updateVnArr = new Vector3[_vnArr.Length];

                    _modelVArr = new Vector4[_vArr.Length];
                    _fArr = new int[parser.FList.Count][];

                    _vnToFArr = new int[parser.VNtoFList.Count][];
                    for (var i = 0; i < parser.VNtoFList.Count; i++)
                    {
                        _vnToFArr[i] = parser.VNtoFList[i].ToArray();
                    }

                    Service.VPolygonNormales = new Vector3[_fArr.Length];

                    for (var i = 0; i < parser.FList.Count; i++)
                    {
                        _fArr[i] = parser.FList[i].ToArray();
                    }

                    pictureBox1.Invalidate();
                }
                else
                {
                    _shouldDraw = false;
                }
            }
        }

        private static void VertexesUpdate()
        {
            Service.UpdateMatrix();
            Service.TranslatePositions(_vArr, _updateVArr, _fArr, _modelVArr, _vnArr, _updateVnArr);
            CleanZBuffer();
            DrawPoints();
        }

        private void Form1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (_shouldDraw)
            {
                if (e.Delta > 0)
                {
                    Service.ScalingCof += Service.Delta;
                }
                else
                {
                    Service.ScalingCof -= Service.Delta;
                }

                Service.Delta = Service.ScalingCof / 3;
                pictureBox1.Invalidate();
            }
        }

        public static unsafe void DrawingFullGrid(BitmapData bData, byte bitsPerPixel, byte* scan0, Color clr)
        {
            foreach (var polygon in _fArr)
            {
                for (var i = 0; i < polygon.Length - 1; i++)
                {
                    var index1 = polygon[i];
                    var index2 = polygon[i + 1];

                    PointF point1 = new PointF(_updateVArr[index1 - 1].X, _updateVArr[index1 - 1].Y);
                    PointF point2 = new PointF(_updateVArr[index2 - 1].X, _updateVArr[index2 - 1].Y);
                    var zZ = (_updateVArr[index1 - 1].Z + _updateVArr[index2 - 1].Z) / 2;
                    DrawLineBresenham(bData, bitsPerPixel, scan0, clr, point1, point2, zZ);
                }

                var lastIndex = polygon[polygon.Length - 1];
                var firstIndex = polygon[0];
                PointF lastPoint = new PointF(_updateVArr[lastIndex - 1].X, _updateVArr[lastIndex - 1].Y);
                PointF firstPoint = new PointF(_updateVArr[firstIndex - 1].X, _updateVArr[firstIndex - 1].Y);
                var z = (_updateVArr[lastIndex - 1].Z + _updateVArr[firstIndex - 1].Z) / 2;
                DrawLineBresenham(bData, bitsPerPixel, scan0, clr, lastPoint, firstPoint, z);
            }
        }

        private static Vector3 CalcPhongBg(float Ka, Vector3 Ia)
        {
            return Ka * Ia;
        }

        private static Vector3 CalcDiffuseLightning(Vector3 lg, Vector3 polygonNormal, float Kd, Vector3 Id)
        {
            return Kd * (Vector3.Dot(Vector3.Normalize(polygonNormal), Vector3.Normalize(lg))) * Id;
        }

        private static Vector3 CalcMirror(float Ks, Vector3 R, Vector3 V, float a, Vector3 Is)
        {
            // R - направление отраженного луча
            // V - направление камеры
            // a - коэффициент зеркальности материала
            // Is - интенсивность и цвет источника света

            // Вычисляем угол между отраженным лучом и направлением камеры
            float cos_alpha = Vector3.Dot(R, V);

            // Если угол между отраженным лучом и направлением камеры меньше нуля, 
            // это значит, что отраженный свет не виден из точки обзора
            if (cos_alpha < 0)
                return Vector3.Zero;

            // Расчет зеркального освещения по модели Фонга
            var specularIntensity = Ks * Is * (float)Math.Pow(cos_alpha, a);

            // Возвращаем вектор цвета зеркального освещения с одинаковой интенсивностью для всех компонентов
            return specularIntensity;
        }

        private static unsafe void RastTriangles(BitmapData bData, byte bitsPerPixel, byte* scan0, Color clr)
        {
            for (var j = 0; j < _fArr.Length; j++)
            {
                var temp = _modelVArr[_fArr[j][0] - 1];
                Vector3 n = new Vector3(temp.X, temp.Y, temp.Z);

                if (Vector3.Dot(Service.VPolygonNormales[j], Service.Camera - n) > 0)
                {
                    var indexes = _fArr[j];
                    var vnIndexes = _vnToFArr[j];

                    Vector4 f1 = _updateVArr[indexes[0] - 1];
                    Vector3 n1 = _updateVnArr[vnIndexes[0] - 1];
                    for (var i = 1; i <= indexes.Length - 2; i++)
                    {
                        Vector4 f2 = _updateVArr[indexes[i] - 1];
                        Vector4 f3 = _updateVArr[indexes[i + 1] - 1];

                        Vector3 n2 = _updateVnArr[vnIndexes[i] - 1];
                        Vector3 n3 = _updateVnArr[vnIndexes[i + 1] - 1];

                        // Определение минимальных и максимальных значений x и y для трех точек
                        var minX = Math.Min(f1.X, Math.Min(f2.X, f3.X));
                        var maxX = Math.Max(f1.X, Math.Max(f2.X, f3.X));
                        var minY = Math.Min(f1.Y, Math.Min(f2.Y, f3.Y));
                        var maxY = Math.Max(f1.Y, Math.Max(f2.Y, f3.Y));

                        // Округление значений x и y до ближайших целых чисел
                        var startX = (int)Math.Ceiling(minX);
                        var endX = (int)Math.Floor(maxX);
                        var startY = (int)Math.Ceiling(minY);
                        var endY = (int)Math.Floor(maxY);
                        // Перебор всех точек внутри ограничивающего прямоугольника
                        for (var y = startY; y <= endY; y++)
                        {
                            for (var x = startX; x <= endX; x++)
                            {
                                // Проверка, принадлежит ли точка треугольнику
                                if (IsPointInTriangle(x, y, f1, f2, f3))
                                {
                                    Vector4 barycentricCoords = CalculateBarycentricCoordinates(x, y, f1, f2, f3);
                                    // Расчет значения z с использованием барицентрических координат
                                    var z = barycentricCoords.X * f1.Z + barycentricCoords.Y * f2.Z +
                                            barycentricCoords.Z * f3.Z;
                                    
                                    var phongClr = CalcPhongBg(Service.Ka, Service.Ia);

                                    var nCl = Color.FromArgb((byte)phongClr.X, (byte)phongClr.Y, (byte)phongClr.Z);
                                    
                                    DrawPoint(bData, bitsPerPixel, scan0, nCl, x + _moving.X, y + _moving.Y, z);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static unsafe void DrawPoint(BitmapData bData, byte bitsPerPixel, byte* scan0, Color cl, float x,
            float y,
            float z)
        {
            var iX = (int)Math.Round(x);
            var iY = (int)Math.Round(y);
            if (x > 0 && x + 1 < _bitmap.Width && y > 0 && y + 1 < _bitmap.Height && _zBuffer[iX][iY] > z)
            {
                _zBuffer[iX][iY] = z;

                var data = scan0 + iY * bData.Stride + iX * bitsPerPixel / 8;
                if (data != null)
                {
                    // Примените интенсивность освещения к цвету пикселя
                    data[0] = cl.B;
                    data[1] = cl.G;
                    data[2] = cl.R;
                }
            }
        }

        private static unsafe void DrawPoint(BitmapData bData, byte bitsPerPixel, byte* scan0, Color cl, float x,
            float y, float z, float intencity)
        {
            var iX = (int)Math.Round(x);
            var iY = (int)Math.Round(y);
            if (x > 0 && x + 1 < _bitmap.Width && y > 0 && y + 1 < _bitmap.Height && _zBuffer[iX][iY] > z)
            {
                _zBuffer[iX][iY] = z;

                var data = scan0 + iY * bData.Stride + iX * bitsPerPixel / 8;
                if (data != null)
                {
                    // Примените интенсивность освещения к цвету пикселя
                    data[0] = (byte)(cl.B * intencity);
                    data[1] = (byte)(cl.G * intencity);
                    data[2] = (byte)(cl.R * intencity);
                }
            }
        }

        // Функция для расчета барицентрических координат
        private static Vector4 CalculateBarycentricCoordinates(int x, int y, Vector4 v1, Vector4 v2, Vector4 v3)
        {
            var alpha = ((v2.Y - v3.Y) * (x - v3.X) + (v3.X - v2.X) * (y - v3.Y)) /
                        ((v2.Y - v3.Y) * (v1.X - v3.X) + (v3.X - v2.X) * (v1.Y - v3.Y));
            var beta = ((v3.Y - v1.Y) * (x - v3.X) + (v1.X - v3.X) * (y - v3.Y)) /
                       ((v2.Y - v3.Y) * (v1.X - v3.X) + (v3.X - v2.X) * (v1.Y - v3.Y));
            var gamma = 1.0f - alpha - beta;

            return new Vector4(alpha, beta, gamma, 0);
        }

        // Функция для проверки, принадлежит ли точка треугольнику
        private static bool IsPointInTriangle(int x, int y, Vector4 v1, Vector4 v2, Vector4 v3)
        {
            var alpha = ((v2.Y - v3.Y) * (x - v3.X) + (v3.X - v2.X) * (y - v3.Y)) /
                        ((v2.Y - v3.Y) * (v1.X - v3.X) + (v3.X - v2.X) * (v1.Y - v3.Y));
            var beta = ((v3.Y - v1.Y) * (x - v3.X) + (v1.X - v3.X) * (y - v3.Y)) /
                       ((v2.Y - v3.Y) * (v1.X - v3.X) + (v3.X - v2.X) * (v1.Y - v3.Y));
            var gamma = 1.0f - alpha - beta;

            return alpha >= 0 && beta >= 0 && gamma >= 0;
        }

        private static unsafe void DrawPoints()
        {
            using (Graphics g = Graphics.FromImage(_bitmap))
            {
                g.Clear(Service.BgColor);
            }

            BitmapData bData = _bitmap.LockBits(new Rectangle(0, 0, _bitmap.Width, _bitmap.Height),
                ImageLockMode.ReadWrite, _bitmap.PixelFormat);
            var bitsPerPixel = (byte)Image.GetPixelFormatSize(bData.PixelFormat);
            var scan0 = (byte*)bData.Scan0;
            //DrawingFullGrid(bData, bitsPerPixel, scan0, whitePen);
            RastTriangles(bData, bitsPerPixel, scan0, Service.SelectedColor);
            _bitmap.UnlockBits(bData);

        }

        private static unsafe void DrawLineBresenham(BitmapData bData, byte bitsPerPixel, byte* scan0, Color clr,
            PointF point1,
            PointF point2, float z)
        {
            var x0 = (int)Math.Round(point1.X);
            var y0 = (int)Math.Round(point1.Y);
            var x1 = (int)Math.Round(point2.X);
            var y1 = (int)Math.Round(point2.Y);

            var dx = Math.Abs(x1 - x0);
            var dy = Math.Abs(y1 - y0);
            var sx = x0 < x1 ? 1 : -1;
            var sy = y0 < y1 ? 1 : -1;
            var err = dx - dy;

            while (true)
            {
                DrawPoint(bData, bitsPerPixel, scan0, clr, x0, y0, z);

                if (x0 == x1 && y0 == y1)
                    break;
                var e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (_shouldDraw)
            {
                const float angel = (float)Math.PI / 15.0f;
                if (e.Control)
                {
                    switch (e.KeyCode)
                    {
                        case Keys.Left:
                            _moving -= MovingX;
                            break;
                        case Keys.Right:
                            _moving += MovingX;
                            break;
                        case Keys.Up:
                            _moving -= MovingY;
                            break;
                        case Keys.Down:
                            _moving += MovingY;
                            break;
                    }

                }
                else
                {
                    switch (e.KeyCode)
                    {
                        case Keys.Left:
                            Translations.Transform(_vArr, Matrix4x4.CreateRotationX(angel));
                            break;
                        case Keys.Right:
                            Translations.Transform(_vArr, Matrix4x4.CreateRotationX(-angel));
                            break;
                        case Keys.Up:
                            Translations.Transform(_vArr, Matrix4x4.CreateRotationY(angel));
                            break;
                        case Keys.Down:
                            Translations.Transform(_vArr, Matrix4x4.CreateRotationY(-angel));
                            break;
                        case Keys.A:
                            Translations.Transform(_vArr, Matrix4x4.CreateRotationZ(angel));
                            break;
                        case Keys.D:
                            Translations.Transform(_vArr, Matrix4x4.CreateRotationZ(-angel));
                            break;
                    }

                }

                pictureBox1.Invalidate();
            }
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            if (_shouldDraw)
            {
                VertexesUpdate();
                pictureBox1.Image = _bitmap;
            }
        }
    }
}