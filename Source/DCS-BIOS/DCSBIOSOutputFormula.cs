﻿namespace DCS_BIOS
{
    using System;
    using System.Collections.Generic;

    using ClassLibraryCommon;

    public class DCSBIOSOutputFormula
    {
        private readonly List<DCSBIOSOutput> _dcsbiosOutputs = new List<DCSBIOSOutput>();
        private readonly Dictionary<string, double> _variables = new Dictionary<string, double>();
        private readonly JaceExtended _jaceExtended = new JaceExtended();
        private string _formula;

        public DCSBIOSOutputFormula()
        {
        }

        public DCSBIOSOutputFormula(string formula)
        {
            _formula = formula;
            ExtractDCSBIOSOutputsInFormula();
        }

        private void ExtractDCSBIOSOutputsInFormula()
        {
            try
            {
                var found = false;
                var controls = DCSBIOSControlLocator.GetControls();
                foreach (var dcsbiosControl in controls)
                {
                    if (_formula.Contains(dcsbiosControl.identifier))
                    {
                        // Console.WriteLine("Variable " + dcsbiosControl.identifier + " set to 0");
                        _variables.Add(dcsbiosControl.identifier, 0);
                        var dcsbiosOutput = DCSBIOSControlLocator.GetDCSBIOSOutput(dcsbiosControl.identifier);
                        _dcsbiosOutputs.Add(dcsbiosOutput);
                        DCSBIOSProtocolParser.RegisterAddressToBroadCast(dcsbiosOutput.Address);
                        found = true;
                    }
                }

                if (!found)
                {
                    throw new Exception("Could not find any DCS-BIOS Controls in formula expression.");
                }
            }
            catch (Exception ex)
            {
                Common.LogError( ex, "ExtractDCSBIOSOutputsInFormula() function");
                throw;
            }
        }

        // Returns true if address was found in formula
        // If true do a subsequent call to Evaluate() to get new value
        public bool CheckForMatch(uint address, uint data)
        {
            try
            {
                var result = false;
                foreach (var dcsbiosOutput in _dcsbiosOutputs)
                {
                    if (dcsbiosOutput.Address == address)
                    {
                        dcsbiosOutput.CheckForValueMatchAndChange(data);
                        result = true;

                        // Console.WriteLine("Variable " + dcsbiosOutput.ControlId + " set to " + dcsbiosOutput.LastIntValue);
                        _variables[dcsbiosOutput.ControlId] = dcsbiosOutput.LastIntValue;

                        //_expression.Parameters[dcsbiosOutput.ControlId] = dcsbiosOutput.LastIntValue;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Common.LogError( ex, "CheckForMatch() function");
                throw;
            }
        }

        public int Evaluate()
        {
            try
            {
                /*Console.WriteLine("_formula : " + _formula);
                foreach (var variable in _variables)
                {
                    Console.WriteLine("variable : " + variable.Key + " = " + variable.Value);
                }*/
                var a = _jaceExtended.CalculationEngine.Calculate(_formula, _variables);
                return Convert.ToInt32(a);
            }
            catch (Exception ex)
            {
                Common.LogError( ex, "Evaluate() function");
            }

            return 99;
        }

        public void ImportString(string str)
        {
            ////DCSBiosOutputFormula{(AAP_EGIPWR+1)/2}
            var value = str;
            if (string.IsNullOrEmpty(str))
            {
                throw new Exception("DCSBiosOutputFormula cannot import null string.");
            }

            if (!str.StartsWith("DCSBiosOutputFormula{") || !str.EndsWith("}"))
            {
                throw new Exception("DCSBiosOutputFormula cannot import string : " + str);
            }

            value = value.Replace("DCSBiosOutputFormula{", string.Empty).Replace("}", string.Empty);

            // (AAP_EGIPWR+1)/2
            _formula = value;

            ExtractDCSBIOSOutputsInFormula();
        }

        public override string ToString()
        {
            return "DCSBiosOutputFormula{" + _formula + "}";
        }

        public string Formula => _formula;
    }
}
