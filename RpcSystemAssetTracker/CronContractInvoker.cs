﻿using Neo.Cryptography.ECC;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Network.RPC;
using Neo.SmartContract;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Neo.Plugins
{

    public partial class RpcSystemAssetTrackerPlugin
    {        
      /// <summary>
      /// Derives an address and keys from given private key 
      /// </summary>
      /// <param name="hexPrivKey">Private key to derive address from</param>
      /// <returns>An object with address and its keys in different formats</returns>
      /// 
        private JObject GetAddress(string hexPrivKey)
        {
            JObject obj = new JObject();
            KeyPair kp = new KeyPair(hexPrivKey.HexToBytes());
            obj["wif"] = kp.Export();
            obj["address"] = kp.AsAddress();
            obj["privkey"] = kp.PrivateKey.ToHexString();
            obj["pubkey"] = kp.PublicKey.ToString(); 
            return obj;
        }

        /// <summary>
        /// Allows to send system assets using a private key
        /// </summary>
        /// <param name="jArray">An array of parameters: 
        /// [0] MANDATORY; HEX private key, string
        /// [1] MANDATORY; destination address, string
        /// [2] MANDATORY; amount to send, double
        /// [3] OPTIONAL;  system token hash (hex bytes string) or its name (string). Default is CRON </param>
        /// <returns>an object with txn_hash field containing transaction hash</returns>

        private JObject Send(JArray jArray)
        {
            JObject obj = new JObject();

            var privateKey = jArray[0].AsString().HexToBytes();
            var addressTo = jArray[1].AsString();

            var amount = (decimal) double.Parse(jArray[2].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture);

            UInt256 th = (jArray.Count > 3) ? ParseTokenHash(jArray[3].AsString()) : Blockchain.UtilityToken.Hash;

            obj["txn_hash"] = SendWithKey(privateKey, amount, addressTo, th);
       
            return obj;
        }
        
        private UInt256 ParseTokenHash(string v)
        {
            v = v.Trim();
            if (v.ToUpper() == "CRON")
                return Blockchain.UtilityToken.Hash;
            if (v.ToUpper() == "CRONIUM")
                return Blockchain.GoverningToken.Hash;
            return UInt256.Parse(v);
        }

        private JObject InvokeSmartContractEntryPointAs(string script, string key, JObject[] jObject)
        {
            JObject[] _params =   jObject.ToArray();
            UInt160 sh = UInt160.Parse(script);
            ContractState contract = Blockchain.Singleton.Store.GetContracts().TryGet(sh);
            if (contract == null)
                throw new RpcException(-1101, $"Smart contract doen't exist: {sh.ToString()}");

            ContractParameter[] parameters = contract
                .ParameterList
                .Select(p => new ContractParameter(p)).ToArray();

            for (int ip = 0; ip < parameters.Length; ip++)
                SetContractParamValue(parameters[ip],
                    ip < _params.Length ? _params[ip] : default(JObject));

            bool b =  CallContract(new KeyPair(key.HexToBytes()), script,  parameters, out byte[] txhash);
            return b ? txhash.ToHexString() : null;
        }

        /// <summary>
        /// SC parameter helper function.
        /// Firstly tries to get SC parameter from type/value,
        /// then from raw value
        /// </summary>
        /// <param name="cp">destination SC parameter</param>
        /// <param name="jObj">source JObject value </param>
        /// 
        private void SetContractParamValue(ContractParameter cp, JObject jObj)
        {
            try
            {
                var cpx = ContractParameter.FromJson(jObj);
                if (cp.Type == cpx.Type)
                {
                    cp.Value = cpx.Value;
                    return;
                }
            }
            catch (Exception ex)
            {
                // Ignore all possible exceptions,
                // then treat raw value without { type, value } object 
            }

            SetFromJsonRaw(cp, jObj);
        }

        /// <summary>
        /// SC parameter helper function.
        /// Tries to get SC parameter of given type from raw value
        /// </summary>
        /// <param name="cp">destination SC parameter</param>
        /// <param name="jObj">source JObject value </param>
        /// 
        private void SetFromJsonRaw(ContractParameter cp, JObject jObj)
        {
            switch (cp.Type)
            {
                case ContractParameterType.Array:
                    cp.Value = jObj == null ?
                    (new ContractParameter[] { }).ToList() :
                    ((JArray)jObj).Select(p => ContractParameter.FromJson(p) ).ToList();
                    break;

                case ContractParameterType.Boolean:
                    cp.Value = bool.Parse(jObj.AsString());
                    break;

                case ContractParameterType.Integer:
                    cp.Value = IntParse(jObj.AsString());
                    break;

                case ContractParameterType.Hash160:
                    cp.Value = UInt160.Parse(jObj.AsString());
                    break;

                case ContractParameterType.Hash256:
                    cp.Value = UInt256.Parse(jObj.AsString());
                    break;

                case ContractParameterType.Signature:
                case ContractParameterType.ByteArray:
                    cp.Value = jObj.AsString().HexToBytes();
                    break;

                case ContractParameterType.PublicKey:
                    cp.Value = ECPoint.Parse(jObj.AsString(), ECCurve.Secp256r1);
                    break;

                case ContractParameterType.String:
                    cp.Value = jObj.AsString();
                    break;

                case ContractParameterType.Map:
                    cp.Value = ((JArray)jObj).Select(p =>
                      new KeyValuePair<ContractParameter, ContractParameter>
                      (ContractParameter.FromJson(p["key"]),
                       ContractParameter.FromJson(p["value"])))
                       .ToList();
                    break;

                case ContractParameterType.InteropInterface:
                    cp.Value = jObj.AsString();
                    break;

                case ContractParameterType.Void:
                    break;

                default:
                    throw new RpcException(-1213, "Wrong parameter type");
            }
        }

        /// <summary>
        /// Parses string as long or BigInteger, 
        /// throws RpcException on error
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        private object IntParse(string v)
        {
            if (long.TryParse(v, out long num))
                return num;
            else if (BigInteger.TryParse(v.Substring(2), NumberStyles.AllowHexSpecifier, null, out BigInteger bi))
                return bi;
            throw new RpcException(-1212, "Parsing integer or BigInteger failed");
        }
    }
}