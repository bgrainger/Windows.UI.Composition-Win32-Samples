﻿//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using Composition.WindowsRuntimeHelpers;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.IO;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Storage.Streams;
using Windows.UI.Composition;

namespace CaptureSampleCore
{
    public class BasicCapture : IDisposable
    {
        private GraphicsCaptureItem item;
        private Direct3D11CaptureFramePool framePool;
        private GraphicsCaptureSession session;
        private SizeInt32 lastSize;

        private IDirect3DDevice device;
        private SharpDX.Direct3D11.Device d3dDevice;
        private SharpDX.DXGI.SwapChain1 swapChain;

        public BasicCapture(IDirect3DDevice d, GraphicsCaptureItem i)
        {
            item = i;
            device = d;
            d3dDevice = Direct3D11Helper.CreateSharpDXDevice(device);

            var size = item.Size;
            if (size.Height == 0 || size.Width == 0)
                size = new SizeInt32() { Height = 1, Width = 1 };

            var dxgiFactory = new SharpDX.DXGI.Factory2();
            var description = new SharpDX.DXGI.SwapChainDescription1()
            {
                Width = size.Width,
                Height =  size.Height,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SharpDX.DXGI.SampleDescription()
                {
                    Count = 1,
                    Quality = 0
                },
                Usage = SharpDX.DXGI.Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = SharpDX.DXGI.Scaling.Stretch,
                SwapEffect = SharpDX.DXGI.SwapEffect.FlipSequential,
                AlphaMode = SharpDX.DXGI.AlphaMode.Premultiplied,
                Flags = SharpDX.DXGI.SwapChainFlags.None
            };
            swapChain = new SharpDX.DXGI.SwapChain1(dxgiFactory, d3dDevice, ref description);

            framePool = Direct3D11CaptureFramePool.Create(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                size);
            session = framePool.CreateCaptureSession(i);
            lastSize = size;

            framePool.FrameArrived += OnFrameArrived;
        }

        public void Dispose()
        {
            session?.Dispose();
            framePool?.Dispose();
            swapChain?.Dispose();
            d3dDevice?.Dispose();
        }

        public void StartCapture()
        {
            session.StartCapture();
        }

        public ICompositionSurface CreateSurface(Compositor compositor)
        {
            return compositor.CreateCompositionSurfaceForSwapChain(swapChain);
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var newSize = false;

            using (var frame = sender.TryGetNextFrame())
            {
                if (frame.ContentSize.Width != lastSize.Width ||
                    frame.ContentSize.Height != lastSize.Height)
                {
                    // The thing we have been capturing has changed size.
                    // We need to resize the swap chain first, then blit the pixels.
                    // After we do that, retire the frame and then recreate the frame pool.
                    newSize = true;
                    lastSize = frame.ContentSize;
                    swapChain.ResizeBuffers(
                        2, 
                        lastSize.Width, 
                        lastSize.Height, 
                        SharpDX.DXGI.Format.B8G8R8A8_UNorm, 
                        SharpDX.DXGI.SwapChainFlags.None);
                }

                using (var backBuffer = swapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0))
                using (var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface))
                {
                    d3dDevice.ImmediateContext.CopyResource(bitmap, backBuffer);
                }

            } // Retire the frame.

            swapChain.Present(0, SharpDX.DXGI.PresentFlags.None);

            if (newSize)
            {
                framePool.Recreate(
                    device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    lastSize);
            }
        }

        //private void GetBitmap(Texture2D texture)
        //{
        //    // Create texture copy
        //    var copy = new Texture2D(d3dDevice, description: new Texture2DDescription
        //    {
        //        Width = texture.Description.Width,
        //        Height = texture.Description.Height,
        //        MipLevels = 1,
        //        ArraySize = 1,
        //        Format = texture.Description.Format,
        //        Usage = ResourceUsage.Staging,
        //        SampleDescription = new SampleDescription(1, 0),
        //        BindFlags = BindFlags.None,
        //        CpuAccessFlags = CpuAccessFlags.Read,
        //        OptionFlags = ResourceOptionFlags.None
        //    });

        //    // Copy data
        //    d3dDevice.ImmediateContext.CopyResource(texture, copy);

        //    var dataBox = d3dDevice.ImmediateContext.MapSubresource(copy, 0, 0, MapMode.Read, MapFlags.None, out DataStream stream);
        //    var rect = new DataRectangle
        //    {
        //        DataPointer = stream.DataPointer,
        //        Pitch = dataBox.RowPitch
        //    };
        
        //    var format = PixelFormat.Format32bppPBGRA;
        //    Bitmap bmp = new Bitmap(factory, copy.Description.Width, copy.Description.Height, format, rect);
        
        //    using (var ras = new InMemoryRandomAccessStream())
        //    {
        //        var ms = ras.AsStream(); // Do not dispose here
        //        using (var wic = new WICStream(factory, ms))
        //        using (var encoder = new PngBitmapEncoder(factory, wic))
        //        using (var frame = new BitmapFrameEncode(encoder))
        //        {
        //            frame.Initialize();
        //            frame.SetSize(bmp.Size.Width, bmp.Size.Height);
        //            frame.SetPixelFormat(ref format);
        //            frame.WriteSource(bmp);
        //            frame.Commit();
        //            encoder.Commit();
        //        }

        //        // BitmapCaptured?.Invoke(this, new CaptureEventArgs(ms, bmp.Size.Width, bmp.Size.Height));
        //    }

        //    d3dDevice.ImmediateContext.UnmapSubresource(copy, 0);
        //    copy.Dispose();
        //    bmp.Dispose();
        //}
    }
}
