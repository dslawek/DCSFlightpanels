﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClassLibraryCommon;

namespace NonVisuals
{
    public class CopyPackage
    {
        private string _sourceName = "";
        private object _content;
        private CopyContentType _copyContentType;


        public string SourceName
        {
            get => _sourceName;
            set => _sourceName = value;
        }

        public object Content
        {
            get => _content;
            set => _content = value;
        }

        public CopyContentType ContentType
        {
            get => _copyContentType;
            set => _copyContentType = value;
        }
    }
}
