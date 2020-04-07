﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.Internal.Utilities;

namespace Microsoft.ML.TimeSeries
{
    public class DTRootCauseAnalyzer
    {
        private static double _anomalyRatioThreshold = 0.5;
        private static double _anomalyDeltaThreshold = 0.95;
        private static double _anomalyPreDeltaThreshold = 2;

        private RootCauseLocalizationInput _src;
        private double _beta;

        public DTRootCauseAnalyzer(RootCauseLocalizationInput src, double beta)
        {
            _src = src;
            _beta = beta;
        }

        public RootCause Analyze()
        {
            return AnalyzeOneLayer(_src);
        }

        //This is a function for analyze one layer for root cause, we select one dimension with values who contributes the most to the anomaly. For full result, call this function recursively
        private RootCause AnalyzeOneLayer(RootCauseLocalizationInput src)
        {
            RootCause dst = new RootCause();
            DimensionInfo dimensionInfo = SeperateDimension(src.AnomalyDimension, src.AggSymbol);
            //no aggregation dimension
            if (dimensionInfo.AggDims.Count == 0)
            {
                return dst;
            }

            Tuple<PointTree, PointTree, Dictionary<string, Point>> pointInfo = GetPointsInfo(src, dimensionInfo, src.AggSymbol);
            PointTree pointTree = pointInfo.Item1;
            PointTree anomalyTree = pointInfo.Item2;
            Dictionary<string, Point> dimPointMapping = pointInfo.Item3;

            //which means there is no anomaly point with the anomaly dimension or no point under anomaly dimension
            if (anomalyTree.ParentNode == null || dimPointMapping.Count == 0)
            {
                return dst;
            }

            dst.Items = new List<RootCauseItem>();
            dst.Items.AddRange(LocalizeRootCauseByDimension(anomalyTree, pointTree, src.AnomalyDimension, dimensionInfo.AggDims));

            GetRootCauseDirectionAndScore(dimPointMapping, src.AnomalyDimension, dst, _beta);

            return dst;
        }

        protected List<Point> GetTotalPointsForAnomalyTimestamp(RootCauseLocalizationInput src)
        {
            List<Point> points = new List<Point>();
            foreach (MetricSlice slice in src.Slices)
            {
                if (slice.TimeStamp.Equals(src.AnomalyTimestamp))
                {
                    points = slice.Points;
                }
            }

            return points;
        }

        protected DimensionInfo SeperateDimension(Dictionary<string, Object> dimensions, string aggSymbol)
        {
            DimensionInfo info = DimensionInfo.CreateDefaultInstance();
            foreach (KeyValuePair<string, Object> entry in dimensions)
            {
                string key = entry.Key;
                if (aggSymbol.Equals(entry.Value))
                {
                    info.AggDims.Add(key);
                }
                else
                {
                    info.DetailDims.Add(key);
                }
            }

            return info;
        }

        protected Tuple<PointTree, PointTree, Dictionary<string, Point>> GetPointsInfo(RootCauseLocalizationInput src, DimensionInfo dimensionInfo, string aggSymbol)
        {
            PointTree pointTree = PointTree.CreateDefaultInstance();
            PointTree anomalyTree = PointTree.CreateDefaultInstance();
            Dictionary<string, Point> dimPointMapping = new Dictionary<string, Point>();

            List<Point> totalPoints = GetTotalPointsForAnomalyTimestamp(src);
            Dictionary<string, Object> subDim = GetSubDim(src.AnomalyDimension, dimensionInfo.DetailDims);

            foreach (Point point in totalPoints)
            {
                if (ContainsAll(point.Dimension, subDim))
                {
                    if (!dimPointMapping.ContainsKey(GetDicCode(point.Dimension)))
                    {
                        dimPointMapping.Add(GetDicCode(point.Dimension), point);
                        bool isValidPoint = point.IsAnomaly == true;
                        if (ContainsAll(point.Dimension, subDim))
                        {
                            BuildTree(pointTree, dimensionInfo.AggDims, point, aggSymbol);

                            if (isValidPoint)
                            {
                                BuildTree(anomalyTree, dimensionInfo.AggDims, point, aggSymbol);
                            }
                        }
                    }
                }
            }

            return new Tuple<PointTree, PointTree, Dictionary<string, Point>>(pointTree, anomalyTree, dimPointMapping);
        }

        protected Dictionary<string, Object> GetSubDim(Dictionary<string, Object> dimension, List<string> keyList)
        {
            Dictionary<string, Object> subDim = new Dictionary<string, Object>();
            foreach (string dim in keyList)
            {
                subDim.Add(dim, dimension[dim]);
            }
            return subDim;
        }

        protected List<RootCauseItem> LocalizeRootCauseByDimension(PointTree anomalyTree, PointTree pointTree, Dictionary<string, Object> anomalyDimension, List<string> aggDims)
        {
            BestDimension best = null;
            if (anomalyTree.ChildrenNodes.Count == 0)
            {
                //has no children node information, should use the leaves node(whose point has no aggrgated dimensions)information
                best = SelectBestDimension(pointTree.Leaves, anomalyTree.Leaves, aggDims);
            }
            else
            {
                //has no leaves information, should calculate the entropy information according to the children nodes
                best = SelectBestDimension(pointTree.ChildrenNodes, anomalyTree.ChildrenNodes, aggDims);
            }

            if (best == null)
            {
                return new List<RootCauseItem>() { new RootCauseItem(anomalyDimension) };
            }

            List<Point> children = null;
            if (anomalyTree.ChildrenNodes.ContainsKey(best.DimensionKey))
            {
                children = GetTopAnomaly(anomalyTree.ChildrenNodes[best.DimensionKey], anomalyTree.ParentNode, pointTree.ChildrenNodes[best.DimensionKey].Count > 0 ? pointTree.ChildrenNodes[best.DimensionKey] : pointTree.Leaves, best.DimensionKey);
            }
            else
            {
                if (best.AnomalyDis.Count > 0)
                {
                    children = new List<Point>();
                    foreach (string dimValue in best.AnomalyDis.Keys.ToArray())
                    {
                        Point p = new Point(UpdateDimensionValue(anomalyDimension, best.DimensionKey, dimValue));
                        children.Add(p);
                    }
                }
            }

            if (children == null)
            {
                //As the cause couldn't be found, the root cause should be itself
                return new List<RootCauseItem>() { new RootCauseItem(anomalyDimension) };
            }
            else
            {
                List<RootCauseItem> causes = new List<RootCauseItem>();
                // For the found causes, we return the result
                foreach (Point anomaly in children)
                {
                    causes.Add(new RootCauseItem(UpdateDimensionValue(anomalyDimension, best.DimensionKey, anomaly.Dimension[best.DimensionKey]), new List<string>() { best.DimensionKey }));
                }
                return causes;
            }
        }

        protected double GetEntropy(int totalNum, int anomalyNum)
        {
            double ratio = (double)anomalyNum / totalNum;
            if (ratio == 0 || ratio == 1)
            {
                return 0;
            }

            return -(ratio * Log2(ratio) + (1 - ratio) * Log2(1 - ratio));
        }

        protected List<Point> GetTopAnomaly(List<Point> anomalyPoints, Point root, List<Point> totalPoints, string dimKey)
        {
            Dictionary<string, int> pointDistribution = new Dictionary<string, int>();
            UpdateDistribution(pointDistribution, totalPoints, dimKey);

            anomalyPoints.OrderBy(x => x.Delta);

            if (root.Delta > 0)
            {
                anomalyPoints.Reverse();
            }

            if (anomalyPoints.Count == 1)
            {
                return anomalyPoints;
            }

            double delta = 0;
            double preDelta = 0;
            List<Point> causeList = new List<Point>();
            foreach (Point anomaly in anomalyPoints)
            {
                if (StopAnomalyComparison(delta, root.Delta, anomaly.Delta, preDelta))
                {
                    break;
                }

                delta += anomaly.Delta;
                causeList.Add(anomaly);
                preDelta = anomaly.Delta;
            }

            int pointSize = GetTotalNumber(pointDistribution);
            if (ShouldSeperateAnomaly(delta, root.Delta, pointSize, causeList.Count))
            {
                return causeList;
            }

            return null;
        }

        protected BestDimension SelectBestDimension(List<Point> totalPoints, List<Point> anomalyPoints, List<string> aggDim)
        {
            double totalEntropy = GetEntropy(totalPoints.Count, anomalyPoints.Count);
            Dictionary<BestDimension, double> entroyGainMap = new Dictionary<BestDimension, double>();
            Dictionary<BestDimension, double> entroyGainRatioMap = new Dictionary<BestDimension, double>();
            double sumGain = 0;

            foreach (string dimKey in aggDim)
            {
                BestDimension dimension = BestDimension.CreateDefaultInstance();
                dimension.DimensionKey = dimKey;

                UpdateDistribution(dimension.PointDis, totalPoints, dimKey);
                UpdateDistribution(dimension.AnomalyDis, anomalyPoints, dimKey);

                double relativeEntropy = GetDimensionEntropy(dimension.PointDis, dimension.AnomalyDis);
                double gain = totalEntropy - relativeEntropy;
                entroyGainMap.Add(dimension, gain);

                double gainRatio = gain / GetDimensionInstrinsicValue(dimension.PointDis);
                entroyGainRatioMap.Add(dimension, gainRatio);

                sumGain += gain;
            }

            double meanGain = sumGain / aggDim.Count();

            BestDimension best = FindBestDimension(entroyGainMap, entroyGainRatioMap, meanGain);
            return best;
        }

        private BestDimension SelectBestDimension(Dictionary<string, List<Point>> pointChildren, Dictionary<string, List<Point>> anomalyChildren, List<string> aggDim)
        {
            Dictionary<BestDimension, double> entropyGainMap = new Dictionary<BestDimension, double>();
            Dictionary<BestDimension, double> entropyGainRatioMap = new Dictionary<BestDimension, double>();
            double sumGain = 0;

            foreach (string dimKey in aggDim)
            {
                BestDimension dimension = BestDimension.CreateDefaultInstance();
                dimension.DimensionKey = dimKey;

                if (pointChildren.ContainsKey(dimKey))
                {
                    UpdateDistribution(dimension.PointDis, pointChildren[dimKey], dimKey);
                }
                if (anomalyChildren.ContainsKey(dimKey))
                {
                    UpdateDistribution(dimension.AnomalyDis, anomalyChildren[dimKey], dimKey);
                }

                double entropy = GetEntropy(dimension.PointDis.Count, dimension.AnomalyDis.Count);
                entropyGainMap.Add(dimension, entropy);

                double gainRatio = entropy / GetDimensionInstrinsicValue(dimension.PointDis);
                entropyGainRatioMap.Add(dimension, gainRatio);

                sumGain += entropy;
            }

            double meanGain = sumGain / aggDim.Count;

            BestDimension best = FindBestDimension(entropyGainMap, entropyGainRatioMap, meanGain, false);
            return best;
        }

        private AnomalyDirection GetRootCauseDirection(Point rootCausePoint)
        {
            if (rootCausePoint.ExpectedValue < rootCausePoint.Value)
            {
                return AnomalyDirection.Up;
            }
            else
            {
                return AnomalyDirection.Down;
            }
        }

        private void GetRootCauseDirectionAndScore(Dictionary<string, Point> dimPointMapping, Dictionary<string, Object> anomalyRoot, RootCause dst, double beta)
        {
            Point anomalyPoint = dimPointMapping[GetDicCode(anomalyRoot)];
            if (dst.Items.Count > 1)
            {
                //get surprise value and explanary power value
                double sumSurprise = 0;
                double sumEp = 0;
                List<RootCauseScore> scoreList = new List<RootCauseScore>();

                foreach (RootCauseItem item in dst.Items)
                {
                    Point rootCausePoint = dimPointMapping[GetDicCode(item.Dimension)];
                    if (anomalyPoint != null && rootCausePoint != null)
                    {
                        Tuple<double, double> scores = GetSupriseAndExplainaryScore(rootCausePoint, anomalyPoint);
                        scoreList.Add(new RootCauseScore(scores.Item1, scores.Item2));
                        sumSurprise += scores.Item1;
                        sumEp += Math.Abs(scores.Item2);

                        item.Direction = GetRootCauseDirection(rootCausePoint);
                    }
                }

                //normalize and get final score
                for (int i = 0; i < scoreList.Count; i++)
                {
                    dst.Items[i].Score = GetFinalScore(scoreList[i].Surprise / sumSurprise, Math.Abs(scoreList[i].ExplainaryScore) / sumEp, beta);

                }
            }
            else if (dst.Items.Count == 1)
            {
                Point rootCausePoint = dimPointMapping[GetDicCode(dst.Items[0].Dimension)];
                if (anomalyPoint != null && rootCausePoint != null)
                {
                    Tuple<double, double> scores = GetSupriseAndExplainaryScore(rootCausePoint, anomalyPoint);
                    dst.Items[0].Score = GetFinalScore(scores.Item1, scores.Item2, beta);
                    dst.Items[0].Direction = GetRootCauseDirection(rootCausePoint);
                }
            }
        }

        private static string GetDicCode(Dictionary<string, Object> dic)
        {
            return string.Join(";", dic.Select(x => x.Key + "=" + (string)x.Value).ToArray());
        }

        private void BuildTree(PointTree tree, List<string> aggDims, Point point, string aggSymbol)
        {
            int aggNum = 0;
            string nextDim = null;

            foreach (string dim in aggDims)
            {
                if (IsAggregationDimension((string)point.Dimension[dim], aggSymbol))
                {
                    aggNum++;
                }
                else
                {
                    nextDim = dim;
                }
            }

            if (aggNum == aggDims.Count)
            {
                tree.ParentNode = point;
            }
            else if (aggNum == aggDims.Count - 1)
            {
                if (!tree.ChildrenNodes.ContainsKey(nextDim))
                {
                    tree.ChildrenNodes.Add(nextDim, new List<Point>());
                }
                tree.ChildrenNodes[nextDim].Add(point);
            }

            if (aggNum == 0)
            {
                tree.Leaves.Add(point);
            }
        }

        private BestDimension FindBestDimension(Dictionary<BestDimension, double> entropyGainMap, Dictionary<BestDimension, double> entropyGainRationMap, double meanGain, bool isLeavesLevel = true)
        {
            BestDimension best = null;
            foreach (KeyValuePair<BestDimension, double> dimension in entropyGainMap)
            {
                if (dimension.Key.AnomalyDis.Count == 1 || dimension.Value <= meanGain)
                {
                    if (dimension.Key.AnomalyDis.Count > 1)
                    {
                        if (best == null || (best.AnomalyDis.Count != 1 && (isLeavesLevel ? entropyGainRationMap[best].CompareTo(dimension.Value) <= 0 : entropyGainRationMap[best].CompareTo(dimension.Value) >= 0)))
                        {
                            best = dimension.Key;
                        }
                    }
                    else
                    {
                        if (best == null || best.AnomalyDis.Count > 1)
                        {
                            best = dimension.Key;
                        }
                        else
                        {
                            if ((isLeavesLevel ? entropyGainRationMap[best].CompareTo(dimension.Value) <= 0 : entropyGainRationMap[best].CompareTo(dimension.Value) >= 0))
                            {
                                best = dimension.Key;
                            }
                        }
                    }
                }
            }

            return best;
        }

        private double GetSurpriseScore(Point rootCausePoint, Point anomalyPoint)
        {
            double p = rootCausePoint.ExpectedValue / anomalyPoint.ExpectedValue;
            double q = rootCausePoint.Value / anomalyPoint.Value;
            double surprise = 0.5 * (p * Log2(2 * p / (p + q)) + q * Log2(2 * q / (p + q)));

            return surprise;
        }

        private double GetFinalScore(double surprise, double ep, double beta)
        {
            return Math.Max(1, beta * surprise + (1 - beta) * ep);
        }

        private Tuple<double, double> GetSupriseAndExplainaryScore(Point rootCausePoint, Point anomalyPoint)
        {
            double surprise = GetSurpriseScore(rootCausePoint, anomalyPoint);
            double ep = (rootCausePoint.Value - rootCausePoint.ExpectedValue) / (anomalyPoint.Value - anomalyPoint.ExpectedValue);

            return new Tuple<double, double>(surprise, ep);
        }

        private static Dictionary<string, Object> UpdateDimensionValue(Dictionary<string, Object> dimension, string key, Object value)
        {
            Dictionary<string, Object> newDim = new Dictionary<string, Object>(dimension);
            newDim[key] = value;
            return newDim;
        }

        private bool StopAnomalyComparison(double preTotal, double parent, double current, double pre)
        {
            if (Math.Abs(preTotal) < Math.Abs(parent) * _anomalyDeltaThreshold)
            {
                return false;
            }

            return Math.Abs(pre) / Math.Abs(current) > _anomalyPreDeltaThreshold;
        }

        private bool ShouldSeperateAnomaly(double total, double parent, int totalSize, int size)
        {
            if (Math.Abs(total) < Math.Abs(parent) * _anomalyDeltaThreshold)
            {
                return false;
            }

            if (size == totalSize && size == 1)
            {
                return true;
            }

            return size <= totalSize * _anomalyRatioThreshold;
        }

        private double GetDimensionEntropy(Dictionary<string, int> pointDis, Dictionary<string, int> anomalyDis)
        {
            int total = GetTotalNumber(pointDis);
            double entropy = 0;

            foreach (string key in anomalyDis.Keys)
            {
                double dimEntropy = GetEntropy(pointDis[key], anomalyDis[key]);
                entropy += dimEntropy * pointDis[key] / total;
            }

            return entropy;
        }

        private double GetDimensionInstrinsicValue(Dictionary<string, int> pointDis)
        {
            int total = GetTotalNumber(pointDis);
            double instrinsicValue = 0;

            foreach (string key in pointDis.Keys)
            {
                instrinsicValue -= Log2((double)pointDis[key] / total) * (double)pointDis[key] / total;
            }

            return instrinsicValue;
        }

        private int GetTotalNumber(Dictionary<string, int> distribution)
        {
            int total = 0;
            foreach (int num in distribution.Values)
            {
                total += num;
            }
            return total;
        }

        private void UpdateDistribution(Dictionary<string, int> distribution, List<Point> points, string dimKey)
        {
            foreach (Point point in points)
            {
                string dimVal = (string)point.Dimension[dimKey];
                if (!distribution.ContainsKey(dimVal))
                {
                    distribution.Add(dimVal, 0);
                }
                distribution[dimVal] = distribution[dimVal] + 1;
            }
        }

        public double Log2(double val)
        {
            return Math.Log(val) / Math.Log(2);
        }

        public static bool ContainsAll(Dictionary<string, Object> bigDic, Dictionary<string, Object> smallDic)
        {
            foreach (var item in smallDic)
            {
                if (!bigDic.ContainsKey(item.Key))
                {
                    return false;
                }

                if (bigDic.ContainsKey(item.Key) && !bigDic[item.Key].Equals(smallDic[item.Key]))
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsAggregationDimension(string val, string aggSymbol)
        {
            return val.Equals(aggSymbol);
        }
    }

    public class DimensionInfo
    {
        public List<string> DetailDims { get; set; }
        public List<string> AggDims { get; set; }

        public static DimensionInfo CreateDefaultInstance()
        {
            DimensionInfo instance = new DimensionInfo();
            instance.DetailDims = new List<string>();
            instance.AggDims = new List<string>();
            return instance;
        }
    }

    public class PointTree
    {
        public Point ParentNode;
        public Dictionary<string, List<Point>> ChildrenNodes;
        public List<Point> Leaves;

        public static PointTree CreateDefaultInstance()
        {
            PointTree instance = new PointTree();
            instance.Leaves = new List<Point>();
            instance.ChildrenNodes = new Dictionary<string, List<Point>>();
            return instance;
        }
    }

    public sealed class BestDimension
    {
        public string DimensionKey;
        public double Entropy;
        public Dictionary<string, int> AnomalyDis;
        public Dictionary<string, int> PointDis;

        public BestDimension() { }
        public static BestDimension CreateDefaultInstance()
        {
            BestDimension instance = new BestDimension();
            instance.AnomalyDis = new Dictionary<string, int>();
            instance.PointDis = new Dictionary<string, int>();
            return instance;
        }
    }

    public class RootCauseScore
    {
        public double Surprise;
        public double ExplainaryScore;

        public RootCauseScore(double surprise, double explainaryScore)
        {
            Surprise = surprise;
            ExplainaryScore = explainaryScore;
        }
    }
}