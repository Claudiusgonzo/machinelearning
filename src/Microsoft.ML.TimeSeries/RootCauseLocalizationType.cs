﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.ML.Data;

namespace Microsoft.ML.TimeSeries
{
    /// <summary>
    /// Allows a member to be marked as a <see cref="RootCauseLocalizationInputDataViewType"/>, primarily allowing one to set
    /// root cause localization input.
    /// </summary>
    public sealed class RootCauseLocalizationInputTypeAttribute : DataViewTypeAttribute
    {
        /// <summary>
        /// Create a root cause localizing input type.
        /// </summary>
        public RootCauseLocalizationInputTypeAttribute()
        {
        }

        /// <summary>
        /// Equal function.
        /// </summary>
        public override bool Equals(DataViewTypeAttribute other)
        {
            if (!(other is RootCauseLocalizationInputTypeAttribute otherAttribute))
                return false;
            return true;
        }

        /// <summary>
        /// Produce the same hash code for all RootCauseLocalizationInputTypeAttribute.
        /// </summary>
        public override int GetHashCode()
        {
            return 0;
        }

        public override void Register()
        {
            DataViewTypeManager.Register(new RootCauseLocalizationInputDataViewType(), typeof(RootCauseLocalizationInput), this);
        }
    }

    /// <summary>
    /// Allows a member to be marked as a <see cref="RootCauseDataViewType"/>, primarily allowing one to set
    /// root cause result.
    /// </summary>
    public sealed class RootCauseTypeAttribute : DataViewTypeAttribute
    {
        /// <summary>
        /// Create an root cause type.
        /// </summary>
        public RootCauseTypeAttribute()
        {
        }

        /// <summary>
        /// RootCauseTypeAttribute with the same type should equal.
        /// </summary>
        public override bool Equals(DataViewTypeAttribute other)
        {
            if (other is RootCauseTypeAttribute otherAttribute)
                return true;
            return false;
        }

        /// <summary>
        /// Produce the same hash code for all RootCauseTypeAttribute.
        /// </summary>
        public override int GetHashCode()
        {
            return 0;
        }

        public override void Register()
        {
            DataViewTypeManager.Register(new RootCauseDataViewType(), typeof(RootCause), this);
        }
    }

    public sealed class RootCause
    {
        public List<RootCauseItem> Items { get; set; }
        public RootCause()
        {
            Items = new List<RootCauseItem>();
        }
    }

    public sealed class RootCauseLocalizationInput
    {
        //When the anomaly incident occurs
        public DateTime AnomalyTimestamp { get; set; }

        //Point with the anomaly dimension must exist in the slice list at the anomaly timestamp, or the libary will not calculate the root cause
        public Dictionary<string, Object> AnomalyDimension { get; set; }

        //A list of points at different timestamp. If the slices don't contain point data corresponding to the anomaly timestamp, the root cause localization alogorithm will not calculate the root cause as no information at the anomaly timestamp is provided.
        public List<MetricSlice> Slices { get; set; }

        //The aggregated symbol in the AnomalyDimension and point dimension should be consistent
        public AggregateType AggType { get; set; }

        public Object AggSymbol { get; set; }

        public RootCauseLocalizationInput(DateTime anomalyTimestamp, Dictionary<string, Object> anomalyDimension, List<MetricSlice> slices, AggregateType aggregateType, Object aggregateSymbol)
        {
            AnomalyTimestamp = anomalyTimestamp;
            AnomalyDimension = anomalyDimension;
            Slices = slices;
            AggType = aggregateType;
            AggSymbol = aggregateSymbol;
        }

        public RootCauseLocalizationInput(DateTime anomalyTimestamp, Dictionary<string, Object> anomalyDimension, List<MetricSlice> slices, string aggregateSymbol)
        {
            AnomalyTimestamp = anomalyTimestamp;
            AnomalyDimension = anomalyDimension;
            Slices = slices;
            AggType = AggregateType.Unknown;
            AggSymbol = aggregateSymbol;
        }
    }

    public sealed class RootCauseDataViewType : StructuredDataViewType
    {
        public RootCauseDataViewType()
           : base(typeof(RootCause))
        {
        }

        public override bool Equals(DataViewType other)
        {
            if (other == this)
                return true;
            if (!(other is RootCauseDataViewType tmp))
                return false;
            return true;
        }

        public override int GetHashCode()
        {
            return 0;
        }

        public override string ToString()
        {
            return typeof(RootCauseDataViewType).Name;
        }
    }

    public sealed class RootCauseLocalizationInputDataViewType : StructuredDataViewType
    {
        public RootCauseLocalizationInputDataViewType()
           : base(typeof(RootCauseLocalizationInput))
        {
        }

        public override bool Equals(DataViewType other)
        {
            if (!(other is RootCauseLocalizationInputDataViewType tmp))
                return false;
            return true;
        }

        public override int GetHashCode()
        {
            return 0;
        }

        public override string ToString()
        {
            return typeof(RootCauseLocalizationInputDataViewType).Name;
        }
    }

    public enum AggregateType
    {
        /// <summary>
        /// Make the aggregate type as unknown type.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// Make the aggregate type as summation.
        /// </summary>
        Sum = 1,
        /// <summary>
        /// Make the aggregate type as average.
        ///  </summary>
        Avg = 2,
        /// <summary>
        /// Make the aggregate type as min.
        /// </summary>
        Min = 3,
        /// <summary>
        /// Make the aggregate type as max.
        /// </summary>
        Max = 4
    }

    public enum AnomalyDirection
    {
        /// <summary>
        /// the value is larger than expected value.
        /// </summary>
        Up = 0,
        /// <summary>
        /// the value is lower than expected value.
        ///  </summary>
        Down = 1,
        /// <summary>
        /// the value is the same as expected value.
        ///  </summary>
        Same = 2
    }

    public sealed class RootCauseItem : IEquatable<RootCauseItem>
    {
        //The score is a value to evaluate the contribution to the anomaly incident. The range is between [0,1]. The larger the score, the root cause contributes the most to the anomaly. The parameter beta has an influence on this score. For how the score is calculated, you can refer to the source code.
        public double Score;
        //Path is a list of the dimension key that the libary selected for you. In this root cause localization library, for one time call for the library, the path will be obtained and the length of path list will always be 1. Different RootCauseItem obtained from one library call will have the same path as it is the best dimension selected for the input.
        public List<string> Path;
        //The dimension for the detected root cause point
        public Dictionary<string, Object> Dimension;
        //The direction for the detected root cause point
        public AnomalyDirection Direction;

        public RootCauseItem(Dictionary<string, Object> rootCause)
        {
            Dimension = rootCause;
            Path = new List<string>();
        }

        public RootCauseItem(Dictionary<string, Object> rootCause, List<string> path)
        {
            Dimension = rootCause;
            Path = path;
        }
        public bool Equals(RootCauseItem other)
        {
            if (Dimension.Count == other.Dimension.Count)
            {
                foreach (KeyValuePair<string, Object> item in Dimension)
                {
                    if (!other.Dimension[item.Key].Equals(item.Value))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }
    }

    public sealed class MetricSlice
    {
        public DateTime TimeStamp { get; set; }
        public List<Point> Points { get; set; }

        public MetricSlice(DateTime timeStamp, List<Point> points)
        {
            TimeStamp = timeStamp;
            Points = points;
        }
    }

    public sealed class Point : IEquatable<Point>
    {
        public double Value { get; set; }
        public double ExpectedValue { get; set; }
        public bool IsAnomaly { get; set; }
        //The value for this dictionary is an object, when the Dimension is used, the equals function for the Object will be used. If you have a customized class, you need to define the Equals function.
        public Dictionary<string, Object> Dimension { get; set; }
        public double Delta { get; set; }

        public Point(Dictionary<string, Object> dimension)
        {
            Dimension = dimension;
        }
        public Point(double value, double expectedValue, bool isAnomaly, Dictionary<string, Object> dimension)
        {
            Value = value;
            ExpectedValue = expectedValue;
            IsAnomaly = isAnomaly;
            Dimension = dimension;
            Delta = value - expectedValue;
        }

        public bool Equals(Point other)
        {
            foreach (KeyValuePair<string, Object> item in Dimension)
            {
                if (!other.Dimension[item.Key].Equals(item.Value))
                {
                    return false;
                }
            }
            return true;
        }

        public override int GetHashCode()
        {
            return Dimension.GetHashCode();
        }
    }
}
