﻿#region License
// 
//     MIT License
//
//     CoiniumServ - Crypto Currency Mining Pool Server Software
//     Copyright (C) 2013 - 2017, CoiniumServ Project
//     Hüseyin Uslu, shalafiraistlin at gmail dot com
//     https://github.com/bonesoul/CoiniumServ
// 
//     Permission is hereby granted, free of charge, to any person obtaining a copy
//     of this software and associated documentation files (the "Software"), to deal
//     in the Software without restriction, including without limitation the rights
//     to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//     copies of the Software, and to permit persons to whom the Software is
//     furnished to do so, subject to the following conditions:
//     
//     The above copyright notice and this permission notice shall be included in all
//     copies or substantial portions of the Software.
//     
//     THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//     IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//     FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//     AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//     LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//     OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//     SOFTWARE.
// 
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using CoiniumServ.Utils.Helpers;
using Dapper;
using MySql.Data.MySqlClient;

namespace CoiniumServ.Persistance.Layers.Hybrid
{
    public partial class HybridStorage
    {
        public IDictionary<string, double> GetHashrateData(int since)
        {
            var hashrates = new Dictionary<string, double>();

            try
            {
                if (!IsEnabled || !_redisProvider.IsConnected)
                    return hashrates;

                var key = string.Format("{0}:hashrate", _coin);

                var results = _redisProvider.Client.ZRangeByScore(key, since, double.PositiveInfinity);

                foreach (var result in results)
                {
                    var data = result.Split(':');
                    var share = double.Parse(data[0].Replace(',', '.'), CultureInfo.InvariantCulture);
                    var worker = data[1].Substring(0, data[1].Length - 8);

                    if (!hashrates.ContainsKey(worker))
                        hashrates.Add(worker, 0);

                    hashrates[worker] += share;
                }
            }
            catch (Exception e)
            {
                _logger.Error("An exception occured while getting hashrate data: {0:l}", e.Message);
            }

            return hashrates;
        }

        public void DeleteExpiredHashrateData(int until)
        {
            try
            {
                if (!IsEnabled || !_redisProvider.IsConnected)
                    return;

                var key = string.Format("{0}:hashrate", _coin);
                _redisProvider.Client.ZRemRangeByScore(key, double.NegativeInfinity, until);
            }
            catch (Exception e)
            {
                _logger.Error("An exception occured while deleting expired hashrate data: {0:l}", e.Message);
            }
        }
        
        public IDictionary<string, double> GetHistoricHashrateData(int hashrateWindow, int window)
        {
            var hashrates = new Dictionary<string, double>();

            try
            {
                if (!IsEnabled || !_redisProvider.IsConnected)
                    return hashrates;

                var newTime = TimeHelpers.RoundUp(DateTime.Now, TimeSpan.FromMinutes(hashrateWindow / 60));
                var key = string.Format("{0}:hashrate", _coin);

                int iterations = (int)Math.Ceiling((double)window / hashrateWindow);
                for (var i = iterations; i > 0; i--)
                {
                    var startTime = TimeHelpers.ToUnixTimestamp(newTime) - i*hashrateWindow - hashrateWindow;
                    var endTime = TimeHelpers.ToUnixTimestamp(newTime) - i*hashrateWindow;
                    var endTimeString = endTime.ToString();
                    
                    var results = _redisProvider.Client.ZRangeByScore(key, startTime, endTime);

                    foreach (var result in results)
                    {
                        var data = result.Split(':');
                        var share = double.Parse(data[0].Replace(',', '.'), CultureInfo.InvariantCulture);
                        var worker = data[1].Substring(0, data[1].Length - 8);

                        if (!hashrates.ContainsKey(endTimeString))
                            hashrates.Add(endTimeString, 0);

                        hashrates[endTimeString] += share;
                    }

                    if (results.Length == 0)
                    {
                        if (!hashrates.ContainsKey(endTimeString))
                            hashrates.Add(endTimeString, 0);

                        hashrates[endTimeString] = 0;
                    }
                }
                
                //_logger.Debug("{0}", hashrates);
            }
            catch (Exception e)
            {
                _logger.Error("An exception occured while getting hashrate data: {0:l}", e.Message);
            }

            return hashrates;
        }
        
        public void AddHistoricValue(List<Dictionary<string, object>> data)
        {
            try
            {
                if (!IsEnabled)
                    return;

                using (var connection = new MySqlConnection(_mySqlProvider.ConnectionString))
                {
                    _logger.Debug("{0}", data);
                    
                    foreach (var query in data)
                    {
                        connection.Execute(
                            @"INSERT INTO Statistics(Type, Domain, Attached, Value, CreatedAt) VALUES (@type, @domain, @attached, @value, @createdAt)",
                            new
                            {
                                type = query["type"],
                                domain = query["domain"],
                                attached = query["attached"],
                                value = query["value"],
                                createdAt = TimeHelpers.NowInUnixTimestamp().UnixTimestampToDateTime()
                            });
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error("An exception occured while adding historic value to MySQL; {0:l}", e.Message);
            }
            
            return;
        }
    }
}
