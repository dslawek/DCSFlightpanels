﻿namespace NonVisuals.StreamDeck
{
    using System;
    using System.Drawing;

    using Newtonsoft.Json;

    using NonVisuals.Interfaces;

    [Serializable]
    public class FaceTypeDCSBIOSOverlay : FaceTypeDCSBIOS
    {
        [JsonProperty("FaceType", Required = Required.Default)]
        public new EnumStreamDeckFaceType FaceType => EnumStreamDeckFaceType.DCSBIOSOverlay;
        private string _backgroundBitmapPath = string.Empty;
        [NonSerialized] private Bitmap _backgroundBitmap;
        private bool _loadBackground;
        private double _dcsbiosValue;

        public FaceTypeDCSBIOSOverlay(StreamDeckPanel streamDeckPanel) : base(streamDeckPanel)
        {
        }

        protected override void DrawBitmap()
        {
            if (_backgroundBitmap == null || _loadBackground)
            {
                _backgroundBitmap = StreamDeckPanel.Validate(_backgroundBitmapPath);
                RefreshBitmap = true;
            }

            if (_bitmap == null || RefreshBitmap)
            {
                _bitmap = BitMapCreator.CreateStreamDeckBitmap(ButtonFinalText, TextFont, FontColor, OffsetX, OffsetY, _backgroundBitmap);
                RefreshBitmap = false;
            }
        }

        protected override void Show()
        {
            DrawBitmap();
            if (StreamDeckPanelInstance == null)
            {
                throw new Exception("StreamDeckPanelInstance is not set, cannot show image [FaceTypeDCSBIOSOverlay]");
            }

            StreamDeckPanelInstance.SetImage(StreamDeckButtonName, Bitmap);
        }

        [JsonProperty("BackgroundBitmapPath", Required = Required.Default)]
        public string BackgroundBitmapPath
        {
            get => _backgroundBitmapPath;
            set
            {
                _backgroundBitmapPath = value;
                _loadBackground = true;
            }
        }

        [JsonIgnore]
        public Bitmap BackgroundBitmap
        {
            get => _backgroundBitmap;
            set => _backgroundBitmap = value;
        }

        [JsonIgnore]
        public double DCSBIOSValue
        {
            get => _dcsbiosValue;
            set
            {
                _dcsbiosValue = value;
                RefreshBitmap = true;
            }
        }
    }
}
