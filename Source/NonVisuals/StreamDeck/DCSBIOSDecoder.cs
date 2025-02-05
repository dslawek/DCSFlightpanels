﻿namespace NonVisuals.StreamDeck
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Windows.Media.Imaging;

    using ClassLibraryCommon;

    using DCS_BIOS;
    using DCS_BIOS.EventArgs;
    using DCS_BIOS.Interfaces;

    using Newtonsoft.Json;

    using NonVisuals.StreamDeck.Events;

    [Serializable]
    public class DCSBIOSDecoder : FaceTypeDCSBIOS, IDcsBiosDataListener, IDCSBIOSStringListener, IDisposable
    {
        private DCSBIOSOutput _dcsbiosOutput;
        private string _formula = string.Empty;
        private bool _useFormula;
        private double _formulaResult = double.MaxValue;
        private string _lastFormulaError = string.Empty;
        private List<DCSBIOSConverter> _dcsbiosConverters = new List<DCSBIOSConverter>();
        private volatile bool _valueUpdated = true;
        [NonSerialized] private int _jaceId;
        private DCSBiosOutputType _decoderSourceType = DCSBiosOutputType.INTEGER_TYPE;
        private bool _treatStringAsNumber;
        private EnumDCSBIOSDecoderOutputType _decoderOutputType = EnumDCSBIOSDecoderOutputType.Raw;

        [NonSerialized] private Thread _imageUpdateTread;
        private bool _shutdown;

        private Bitmap _converterBitmap;

        public DCSBIOSDecoder(StreamDeckPanel streamDeckPanel) : base(streamDeckPanel)
        {
            _jaceId = RandomFactory.Get();
            _imageUpdateTread = new Thread(ImageRefreshingThread);
            _imageUpdateTread.Start();
            DCSBIOS.GetInstance().AttachDataReceivedListener(this);
            EventHandlers.AttachDCSBIOSDecoder(this);
        }

        public override void Dispose()
        {
            EventHandlers.DetachDCSBIOSDecoder(this);
            DCSBIOSStringManager.DetachListener(this);
            DCSBIOS.GetInstance()?.DetachDataReceivedListener(this);
            _shutdown = true;

            try
            {
                if (_imageUpdateTread != null && (_imageUpdateTread.ThreadState & (ThreadState.Aborted | ThreadState.AbortRequested)) == 0)
                {
                    _imageUpdateTread.Abort();
                }
            }
            catch (Exception)
            {
                // ignored
            }

            base.Dispose();
        }

        [JsonIgnore]
        public override string FaceDescription
        {
            get
            {
                var stringBuilder = new StringBuilder(100);
                stringBuilder.Append("Face DCS-BIOS Decoder");
                if (_dcsbiosOutput != null)
                {
                    stringBuilder.Append(" ").Append(_dcsbiosOutput.ControlId);
                }

                return stringBuilder.ToString();
            }
        }

        public static void ShowOnly(DCSBIOSDecoder dcsbiosDecoder, StreamDeckPanel streamDeckPanel)
        {
            EventHandlers.HideDCSBIOSDecoders(dcsbiosDecoder, streamDeckPanel.SelectedLayerName, streamDeckPanel.BindingHash);
            dcsbiosDecoder.IsVisible = true;
        }

        public void HideAllEvent(object sender, StreamDeckHideDecoderEventArgs e)
        {
            if (StreamDeckPanelInstance.BindingHash == e.BindingHash && StreamDeckButtonName == e.StreamDeckButtonName)
            {
                IsVisible = false;
            }
        }

        public override void AfterClone()
        {
            DCSBIOS.GetInstance().AttachDataReceivedListener(this);

            // _autoResetEvent = new AutoResetEvent(false);
            if (_imageUpdateTread != null)
            {
                try
                {
                    _imageUpdateTread.Abort();
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            _imageUpdateTread = new Thread(ImageRefreshingThread);
            _imageUpdateTread.Start();
        }

        private void ImageRefreshingThread()
        {
            while (!_shutdown)
            {
                if (!IsVisible)
                {
                    /*
                     * If decoder isn't visible we end up here until it is visible again
                     */
                    // _autoResetEvent.WaitOne();
                }

                if (_shutdown)
                {
                    break;
                }

                if (ValueUpdated)
                {
                    HandleNewDCSBIOSValue();
                }

                Thread.Sleep(StreamDeckConstants.IMAGE_UPDATING_THREAD_SLEEP_TIME);
            }
        }

        public void DcsBiosDataReceived(object sender, DCSBIOSDataEventArgs e)
        {
            try
            {
                if (_decoderSourceType == DCSBiosOutputType.STRING_TYPE)
                {
                    return;
                }

                if (_dcsbiosOutput?.Address == e.Address)
                {
                    if (!Equals(UintDcsBiosValue, e.Data))
                    {
                        UintDcsBiosValue = _dcsbiosOutput.GetUIntValue(e.Data);
                        _valueUpdated = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, "DcsBiosDataReceived()");
            }
        }


        public void DCSBIOSStringReceived(object sender, DCSBIOSStringDataEventArgs e)
        {
            try
            {
                if (_decoderSourceType == DCSBiosOutputType.INTEGER_TYPE)
                {
                    return;
                }

                if (_dcsbiosOutput?.Address == e.Address)
                {
                    if (TreatStringAsNumber && string.IsNullOrWhiteSpace(e.StringData))
                    {
                        StringDcsBiosValue = "0";
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(e.StringData))
                        {
                            StringDcsBiosValue = string.Empty;
                        }
                        else if (e.StringData.Length < _dcsbiosOutput.MaxLength)
                        {
                            StringDcsBiosValue = e.StringData.Substring(0, e.StringData.Length);
                        }
                        else
                        {
                            StringDcsBiosValue = e.StringData.Substring(0, _dcsbiosOutput.MaxLength);
                        }
                    }

                    /*
                     * If DCS-BIOS sends null string data and the decoder should 
                     * treat it as number then it will be represented by zero.
                     */
                    if (_treatStringAsNumber && uint.TryParse(string.IsNullOrWhiteSpace(e.StringData) ? "0" : e.StringData.Substring(0, _dcsbiosOutput.MaxLength), out var tmpUint))
                    {
                        UintDcsBiosValue = tmpUint;
                    }

                    _valueUpdated = true;
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, "DCSBIOSStringReceived()");
            }
        }

        /*
         * 1) integer
         * 2) string but treat as integer
         * 3) string and treat it as string (no formulas, no converters)
         */
        public void HandleNewDCSBIOSValue()
        {
            try
            {
                if (UseFormula)
                {
                    _formulaResult = EvaluateFormula();
                }

                /*
                 * 1) Use decoder raw  (formula / no formula) STRING_TYPE or INTEGER_TYPE
                 * 2) Use converter    (formula / no formula)
                 * 3) show blank image
                 */
                var showImage = false;

                /*   1) Use decoder raw(formula / no formula)  */
                if (_dcsbiosConverters.Count == 0)
                {
                    if (UseFormula)
                    {
                        ButtonFinalText = ButtonTextTemplate.Replace(StreamDeckConstants.DCSBIOSValuePlaceHolder, _formulaResult.ToString(CultureInfo.InvariantCulture));
                        showImage = true;
                    }
                    else if (DecoderSourceType == DCSBiosOutputType.STRING_TYPE && !TreatStringAsNumber)
                    {
                        ButtonFinalText = ButtonTextTemplate.Replace(StreamDeckConstants.DCSBIOSValuePlaceHolder, string.IsNullOrWhiteSpace(StringDcsBiosValue) ? string.Empty : StringDcsBiosValue);
                        showImage = true;
                    }
                    else if (!string.IsNullOrEmpty(ButtonTextTemplate))
                    {
                        ButtonFinalText = ButtonTextTemplate.Replace(StreamDeckConstants.DCSBIOSValuePlaceHolder, UintDcsBiosValue.ToString(CultureInfo.InvariantCulture));
                        showImage = true;
                    }

                    if (IsVisible)
                    {
                        if (showImage)
                        {
                            Show();
                        }
                        else
                        {
                            BlackoutKey();
                        }
                    }
                }

                /* 2) Use converter    (formula / no formula) */
                else if (_dcsbiosConverters.Count > 0 && (_decoderSourceType == DCSBiosOutputType.STRING_TYPE && _treatStringAsNumber) || _decoderSourceType == DCSBiosOutputType.INTEGER_TYPE)
                {
                    foreach (var dcsbiosConverter in _dcsbiosConverters)
                    {
                        if (dcsbiosConverter.CriteriaFulfilled(UseFormula ? FormulaResult : UintDcsBiosValue))
                        {
                            _converterBitmap = dcsbiosConverter.Get();
                            break;
                        }
                    }

                    if (IsVisible)
                    {
                        if (_converterBitmap != null)
                        {
                            ShowBitmap(_converterBitmap);
                        }
                        else
                        {
                            BlackoutKey();
                        }
                    }
                }

                /* 3) show blank image */
                else
                {
                    if (IsVisible)
                    {
                        BlackoutKey();
                    }
                }

                _lastFormulaError = string.Empty;
            }
            catch (Exception exception)
            {
                // Common.LogError(exception);
                _lastFormulaError = exception.Message;
            }
        }


        [JsonProperty("UseFormula", Required = Required.Default)]
        public bool UseFormula
        {
            get => _useFormula;
            set => _useFormula = value;
        }

        private void BlackoutKey()
        {
            StreamDeckPanelInstance.ClearFace(StreamDeckButtonName);
        }

        private void ShowBitmap(Bitmap bitmap)
        {
            if (StreamDeckPanelInstance == null)
            {
                throw new Exception("StreamDeckPanelInstance is not set, cannot show image [DCSBIOSDecoder]");
            }

            StreamDeckPanelInstance.SetImage(StreamDeckButtonName, bitmap);
        }

        private void ShowBitmapImage(BitmapImage bitmapImage)
        {
            if (StreamDeckPanelInstance == null)
            {
                throw new Exception("StreamDeckPanelInstance is not set, cannot show image [DCSBIOSDecoder]");
            }

            StreamDeckPanelInstance.SetImage(StreamDeckButtonName, bitmapImage);
        }

        [JsonIgnore]
        public new StreamDeckPanel StreamDeckPanelInstance
        {
            get => base.StreamDeckPanelInstance;
            set
            {
                base.StreamDeckPanelInstance = value;
                foreach (var dcsbiosConverter in _dcsbiosConverters)
                {
                    dcsbiosConverter.StreamDeckPanelInstance = base.StreamDeckPanelInstance;
                }
            }
        }

        public void RemoveDCSBIOSOutput()
        {
            _dcsbiosOutput = null;
        }

        public void Clear()
        {
            _formula = string.Empty;
            _dcsbiosOutput = null;
            _dcsbiosConverters.Clear();
            _valueUpdated = false;
            _lastFormulaError = string.Empty;
            _formulaResult = 0;
        }

        private double EvaluateFormula()
        {
            // 360 - floor((HSI_HDG / 65535) * 360)
            var variables = new Dictionary<string, double>();
            variables.Add(_dcsbiosOutput.ControlId, 0);
            variables[_dcsbiosOutput.ControlId] = UintDcsBiosValue;
            return JaceExtendedFactory.Instance(ref _jaceId).CalculationEngine.Calculate(_formula, variables);
        }

        [JsonProperty("Formula", Required = Required.Default)]
        public string Formula
        {
            get => _formula;
            set => _formula = value;
        }

        [JsonProperty("DCSBIOSOutput", Required = Required.Default)]
        public DCSBIOSOutput DCSBIOSOutput
        {
            get => _dcsbiosOutput;
            set
            {
                /*
                 * Can be of two types, integer or string output
                 */
                _valueUpdated = true;
                _dcsbiosOutput = value;
                UintDcsBiosValue = uint.MaxValue;
                StringDcsBiosValue = string.Empty;
                if (_dcsbiosOutput.DCSBiosOutputType == DCSBiosOutputType.STRING_TYPE)
                {
                    DCSBIOSStringManager.AddListener(_dcsbiosOutput, this);
                }
            }
        }

        public void Add(DCSBIOSConverter dcsbiosConverter)
        {
            _dcsbiosConverters.Add(dcsbiosConverter);
        }

        [JsonProperty("ImageFiles", Required = Required.Default)]
        public List<string> ImageFiles
        {
            get
            {
                var result = new List<string>();

                foreach (var dcsbiosConverter in _dcsbiosConverters)
                {
                    if (!string.IsNullOrEmpty(dcsbiosConverter.ImageFileRelativePath))
                    {
                        result.Add(dcsbiosConverter.ImageFileRelativePath);
                    }
                }

                return result;
            }
        }

        /*
         * When exporting buttons the image file's path will be removed.
         * When user import the buttons and specifies to where the images should
         * be stored then the path is updated again.
         *
         * So there will only be the filename in ImageFileRelativePath.
         */
        public void ResetImageFilePaths()
        {
            foreach (var dcsbiosConverter in _dcsbiosConverters)
            {
                if (!string.IsNullOrEmpty(dcsbiosConverter.ImageFileRelativePath))
                {
                    dcsbiosConverter.ImageFileRelativePath = Path.GetFileName(dcsbiosConverter.ImageFileRelativePath);
                }
            }
        }

        public void SetImageFilePaths(string path)
        {
            foreach (var dcsbiosConverter in _dcsbiosConverters)
            {
                if (dcsbiosConverter.ConverterOutputType == EnumConverterOutputType.Image || dcsbiosConverter.ConverterOutputType == EnumConverterOutputType.ImageOverlay)
                {
                    dcsbiosConverter.ImageFileRelativePath = path + "\\" + Path.GetFileName(dcsbiosConverter.ImageFileRelativePath);
                }
            }
        }

        public void Replace(DCSBIOSConverter oldDcsBiosValueToFaceConverter, DCSBIOSConverter newDcsBiosValueToFaceConverter)
        {
            Remove(oldDcsBiosValueToFaceConverter);
            Add(newDcsBiosValueToFaceConverter);
        }

        public void Remove(DCSBIOSConverter dcsbiosConverter)
        {
            _dcsbiosConverters.Remove(dcsbiosConverter);
        }

        [JsonProperty("DCSBIOSConverters", Required = Required.Default)]
        public List<DCSBIOSConverter> DCSBIOSConverters
        {
            get => _dcsbiosConverters;
            set => _dcsbiosConverters = value;
        }

        [JsonIgnore]
        public bool ValueUpdated
        {
            get
            {
                var result = false;
                if (_valueUpdated)
                {
                    result = true;
                    _valueUpdated = false; // Reset so next read without update will give false
                }

                return result;
            }
        }

        [JsonIgnore]
        public bool HasErrors => !string.IsNullOrEmpty(_lastFormulaError);

        [JsonIgnore]
        public string LastFormulaError => _lastFormulaError;

        [JsonIgnore]
        public double FormulaResult => _formulaResult;

        public string GetFriendlyInfo()
        {
            return _dcsbiosOutput.ControlId;
        }

        [JsonProperty("TreatStringAsNumber", Required = Required.Default)]
        public bool TreatStringAsNumber
        {
            get => _treatStringAsNumber;
            set => _treatStringAsNumber = value;
        }

        [JsonProperty("RawTextFont", Required = Required.Default)]
        public Font RawTextFont
        {
            get => TextFont;
            set => TextFont = value;
        }

        [JsonProperty("RawFontColor", Required = Required.Default)]
        public Color RawFontColor
        {
            get => FontColor;
            set => FontColor = value;
        }

        [JsonProperty("RawBackgroundColor", Required = Required.Default)]
        public Color RawBackgroundColor
        {
            get => BackgroundColor;
            set => BackgroundColor = value;
        }

        [JsonProperty("DecoderSourceType", Required = Required.Default)]
        public DCSBiosOutputType DecoderSourceType
        {
            get => _decoderSourceType;
            set => _decoderSourceType = value;
        }

        [JsonProperty("DecoderOutputType", Required = Required.Default)]
        public EnumDCSBIOSDecoderOutputType DecoderOutputType
        {
            get => _decoderOutputType;
            set => _decoderOutputType = value;
        }


        /*
         * It can have integer | string + treat as number | string input
         * It can have raw / converter output
         */
        public bool DecoderConfigurationOK()
        {
            var formulaIsOK = !_useFormula || !string.IsNullOrEmpty(_formula);
            var sourceIsOK = _dcsbiosOutput != null;
            var convertersOK = _dcsbiosConverters.FindAll(o => o.FaceConfigurationIsOK == false).Count == 0;


            switch (DecoderOutputType)
            {
                case EnumDCSBIOSDecoderOutputType.Raw:
                    {
                        return formulaIsOK && sourceIsOK && ConfigurationOK;
                    }

                case EnumDCSBIOSDecoderOutputType.Converter:
                    {
                        return formulaIsOK && sourceIsOK && convertersOK;
                    }

                default:
                    {
                        return false;
                    }
            }
        }

        /*
         * Remove settings not relevant based on output type
         */
        public void Clean()
        {
            switch (DecoderOutputType)
            {
                case EnumDCSBIOSDecoderOutputType.Raw:
                    {
                        _dcsbiosConverters.Clear();
                        if (!_useFormula)
                        {
                            _formula = string.Empty;
                        }

                        break;
                    }

                case EnumDCSBIOSDecoderOutputType.Converter:
                    {
                        break;
                    }
            }
        }

        [JsonIgnore]
        public override bool IsVisible
        {
            get => base.IsVisible;
            set
            {
                base.IsVisible = value;
                if (IsVisible)
                {
                    if (_converterBitmap != null)
                    {
                        ShowBitmap(_converterBitmap);
                    }
                    else
                    {
                        BlackoutKey();
                    }
                }
            }
        }

        [JsonProperty("DefaultImageFilePath", Required = Required.Default)]
        public string DefaultImageFilePath
        {
            // No getter, this is to be phased out, setter here so that any existing setting in user's file still can be parsed by JSON.
            set
            {
                var notUsedAnymoreDefaultImageFilePath = value;
            }
        }
    }

    public enum EnumDCSBIOSDecoderOutputType
    {
        Raw,
        Converter
    }

}
