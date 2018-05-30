using System;
using System.Collections.Generic;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using Windows.Foundation.Collections;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Storage;
using Windows.Media;
using Windows.UI;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas;
using Windows.AI.MachineLearning.Preview;
using System.Diagnostics;
using System.Numerics;
using System.Timers;


namespace segm_video_effect_uwp
{
    public sealed class SegmVideoEffect : IBasicVideoEffect
    {

        private const string _kModelFileName = "segm_basic.onnx";
        private const int maxDelay = 50;

        private CanvasDevice canvasDevice;

        private LearningModelPreview _learningModel;

        private VideoFrame output;

        private Timer fpsTimer;
        private int frameCount;
        private double currentFPS;

        private TimeSpan streamStartDelta;

        private Color fpsLabelColor;
        private Vector2 fpsLabelOffset;

        private Dictionary<string, object> features;

        public bool IsReadOnly { get { return false; } }

        public MediaMemoryTypes SupportedMemoryTypes { get { return MediaMemoryTypes.Gpu; } }

        public bool TimeIndependent { get { return true; } }

        private IPropertySet configuration;
        public void SetProperties(IPropertySet configuration)
        {
            this.configuration = configuration;

            var modelFile = StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///segm_video_effect_uwp/Assets/{_kModelFileName}")).GetAwaiter().GetResult();
            Debug.WriteLine(modelFile);

            _learningModel = LearningModelPreview.LoadModelFromStorageFileAsync(modelFile).GetAwaiter().GetResult();

            InferencingOptionsPreview options = _learningModel.InferencingOptions;

            options.PreferredDeviceKind = LearningModelDeviceKindPreview.LearningDeviceGpu;
            //options.IsTracingEnabled = true;

            _learningModel.InferencingOptions = options;

            fpsTimer = new Timer(1000);
            fpsTimer.Elapsed += this.OnTimedEvent;
            fpsTimer.AutoReset = true;
            fpsTimer.Start();

            fpsLabelOffset = new Vector2(10, 10);
            fpsLabelColor = Color.FromArgb(255, 255, 0, 0);
        }

        public void SetEncodingProperties(VideoEncodingProperties encodingProperties, IDirect3DDevice device)
        {
            canvasDevice = CanvasDevice.CreateFromDirect3D11Device(device);
            output = VideoFrame.CreateAsDirect3D11SurfaceBacked(DirectXPixelFormat.R8G8B8A8UIntNormalized, 128, 96, canvasDevice);

            features = new Dictionary<string, object>();
            features.Add("0", null);
            features.Add("794", output);

        }

        public IReadOnlyList<VideoEncodingProperties> SupportedEncodingProperties
        {
            get
            {
                var encodingProperties = new VideoEncodingProperties();
                encodingProperties.Subtype = "ARGB32";
                return new List<VideoEncodingProperties>() { encodingProperties };
            }
        }

        private void OnTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            currentFPS = getFramesCount();
            Debug.WriteLine("Frame rate: " + currentFPS);

        }

        public void Close(MediaEffectClosedReason reason)
        {
            if (fpsTimer != null)
            {
                fpsTimer.Stop();
                fpsTimer.Dispose();
            }

        }

        public void DiscardQueuedFrames()
        {
            frameCount = 0;
        } 

        public int getFramesCount()
        {
            var i = frameCount;
            frameCount = 0;
            return i;
        }


        public void ProcessFrame(ProcessVideoFrameContext context)
        {
            bool skipMaskPred = false;
            if (context.InputFrame.IsDiscontinuous)
            {
                streamStartDelta = TimeSpan.FromTicks(DateTime.Now.Ticks) - context.InputFrame.SystemRelativeTime.Value;
            }
            else
            {
                if ((TimeSpan.FromTicks(DateTime.Now.Ticks) - context.InputFrame.SystemRelativeTime.Value - streamStartDelta) > TimeSpan.FromMilliseconds(maxDelay))
                {
                    skipMaskPred = true;
                }
            }
            
           if (!skipMaskPred)
           {
                frameCount++;
                features["0"] = context.InputFrame;
                var resTask = _learningModel.EvaluateFeaturesAsync(features, string.Empty).AsTask();
                var startTime = DateTime.Now.Ticks;
                resTask.Wait();
                Debug.WriteLine("delta {0}", TimeSpan.FromTicks(DateTime.Now.Ticks - startTime));
            }

            using (CanvasBitmap inputBitmap = CanvasBitmap.CreateFromDirect3D11Surface(canvasDevice, context.InputFrame.Direct3DSurface))
            using (CanvasBitmap inputMask = CanvasBitmap.CreateFromDirect3D11Surface(canvasDevice, output.Direct3DSurface))
            using (CanvasRenderTarget renderTarget = CanvasRenderTarget.CreateFromDirect3D11Surface(canvasDevice, context.OutputFrame.Direct3DSurface))
            using (CanvasDrawingSession ds = renderTarget.CreateDrawingSession())
            {
                ds.Clear(Colors.Green);

                var addAlpha = new ColorMatrixEffect()
                {
                    Source = inputMask,
                    ColorMatrix = RToAlpha
                };

                var resize = new ScaleEffect()
                {
                    Source = addAlpha,
                    Scale = new Vector2(((float)inputBitmap.SizeInPixels.Width / inputMask.SizeInPixels.Width), ((float)inputBitmap.SizeInPixels.Height / inputMask.SizeInPixels.Height))
                };

                var blend = new AlphaMaskEffect()
                {
                    Source = inputBitmap,
                    AlphaMask = resize
                };

                ds.DrawImage(blend);
                ds.DrawText(String.Format("FPS: {0:f1}", currentFPS), fpsLabelOffset, fpsLabelColor);

            }

        }

        private Matrix5x4 RToAlpha = new Matrix5x4
        {
            M11 = 1,
            M12 = 0,
            M13 = 0,
            M14 = 1,
            M21 = 0,
            M22 = 0,
            M23 = 0,
            M24 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 0,
            M34 = 0,
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 0,
            M51 = 0,
            M52 = 0,
            M53 = 0,
            M54 = 0
        };

    }
}
