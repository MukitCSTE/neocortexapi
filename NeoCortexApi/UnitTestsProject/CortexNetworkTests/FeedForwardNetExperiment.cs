using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoCortexApi;
using NeoCortexApi.Classifiers;
using NeoCortexApi.Encoders;
using NeoCortexApi.Entities;
using NeoCortexApi.Network;
using NeoCortexApi.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnitTestsProject.CortexNetworkTests
{

    /// <summary>
    /// In the brain the Layer 4 has feed forforward connection with Layer 2 in CortexLayer.
    /// So, instead of using layer name L1 we give it as L4
    /// </summary>

    [TestClass]
    public class FeedForwardNetExperiment
    {
        CortexLayer<object, object> layerL4, layerL2;

        TemporalMemory tm4, tm2;


        [TestMethod]
        public void FeedForwardNetTest()
        {
            int cellsPerColumnL4 = 20;
            int numColumnsL4 = 1024;

            int cellsPerColumnL2 = 10;
            int numColumnsL2 = 150;

            int inputBits = 100;
            double minOctOverlapCycles = 1.0;
            double maxBoost = 10.0;
            double max = 20;

            HtmConfig htmConfig_L4 = new HtmConfig(new int[] { inputBits }, new int[] { numColumnsL4 })
            {
                Random = new ThreadSafeRandom(42),
                CellsPerColumn = cellsPerColumnL4,
                GlobalInhibition = true,
                LocalAreaDensity = -1,
                NumActiveColumnsPerInhArea = 0.02 * numColumnsL4,
                PotentialRadius = 50, // Ever column is connected to 50 of 100 input cells.
                InhibitionRadius = 15,
                MaxBoost = maxBoost,
                DutyCyclePeriod = 25,
                MinPctOverlapDutyCycles = minOctOverlapCycles,
                MaxSynapsesPerSegment = (int)(0.02 * numColumnsL4),
                ActivationThreshold = 15,
                ConnectedPermanence = 0.5,
                PermanenceDecrement = 0.25,
                PermanenceIncrement = 0.15,
                PredictedSegmentDecrement = 0.1
            };

            // The HTM of the L2 is connected to cells of the HTM of L4.
            int inputsL2 = numColumnsL4 * cellsPerColumnL4;

            HtmConfig htmConfig_L2 = new HtmConfig(new int[] { inputsL2 }, new int[] { numColumnsL2 })
            {
                Random = new ThreadSafeRandom(42),

                CellsPerColumn = cellsPerColumnL2,
                GlobalInhibition = true,
                LocalAreaDensity = -1,
                NumActiveColumnsPerInhArea = 0.5 * numColumnsL2,
                PotentialRadius =  inputsL2, // Every columns 
                InhibitionRadius = 15,
                MaxBoost = maxBoost,
                DutyCyclePeriod = 25,
                MinPctOverlapDutyCycles = minOctOverlapCycles,
                MaxSynapsesPerSegment = (int)(0.2 * numColumnsL2),
                ActivationThreshold = 10,
                ConnectedPermanence = 0.5,
                PermanenceDecrement = 0.25,
                PermanenceIncrement = 0.15,
                PredictedSegmentDecrement = 0.1
            };

            Dictionary<string, object> settings = new Dictionary<string, object>()
            {
                { "W", 15},
                { "N", inputBits},
                { "Radius", -1.0},
                { "MinVal", 0.0},
                { "Periodic", false},
                { "Name", "scalar"},
                { "ClipInput", false},
                { "MaxVal", max}
            };

            EncoderBase encoder = new ScalarEncoder(settings);
            List<double> inputValues = new List<double>(new double[] { 1, 2, 3, 4, 5, 2, 3, 6 });
            RunExperiment(inputBits, htmConfig_L4, encoder, inputValues, htmConfig_L2);
        }

        private void RunExperiment(int inputBits, HtmConfig cfgL4, EncoderBase encoder, List<double> inputValues, HtmConfig cfgL2)
        {
            Stopwatch swL2 = new Stopwatch();

            int maxMatchCnt = 0;
            bool learn = true;
            bool isSP4Stable = false;
            bool isSP2STable = false;

            var memL4 = new Connections(cfgL4);
            var memL2 = new Connections(cfgL2);

            var numInputs = inputValues.Distinct<double>().ToList().Count;
            HtmClassifier<string, ComputeCycle> cls = new HtmClassifier<string, ComputeCycle>();

            layerL4 = new CortexLayer<object, object>("L4");
            layerL2 = new CortexLayer<object, object>("L2");

            tm4 = new TemporalMemoryMT();
            tm2 = new TemporalMemoryMT();

            // HPC for Layer 4 SP

            HomeostaticPlasticityController hpa_sp_L4 = new HomeostaticPlasticityController(memL4, numInputs * 50, (isStable, numPatterns, actColAvg, seenInputs) =>
            {
                if (isStable)
                    Debug.WriteLine($"SP L4 STABLE: Patterns: {numPatterns}, Inputs: {seenInputs}, iteration: {seenInputs / numPatterns}");
                else
                    Debug.WriteLine($"SP L4 INSTABLE: Patterns: {numPatterns}, Inputs: {seenInputs}, iteration: {seenInputs / numPatterns}");
                learn = isSP4Stable = isStable;

            }, numOfCyclesToWaitOnChange: 50);


            // HPC for Layer 2 SP

            HomeostaticPlasticityController hpa_sp_L2 = new HomeostaticPlasticityController(memL2, numInputs * 50, (isStable, numPatterns, actColAvg, seenInputs) =>
            {
                if (isStable)
                    Debug.WriteLine($"SP L2 STABLE: Patterns: {numPatterns}, Inputs: {seenInputs}, iteration: {seenInputs / numPatterns}");
                else
                    Debug.WriteLine($"SP L2 INSTABLE: Patterns: {numPatterns}, Inputs: {seenInputs}, iteration: {seenInputs / numPatterns}");

                learn = isSP2STable = isStable;
                cls.ClearState();
            }, numOfCyclesToWaitOnChange: 50);

            SpatialPooler sp4 = new SpatialPoolerMT(hpa_sp_L4);

            SpatialPooler sp2 = new SpatialPoolerMT(hpa_sp_L2);

            sp4.Init(memL4);
            sp2.Init(memL2);

            // memL2.TraceInputPotential();

            tm4.Init(memL4);
            tm2.Init(memL2);

            layerL4.HtmModules.Add("encoder", encoder);
            layerL4.HtmModules.Add("sp", sp4);
            layerL4.HtmModules.Add("tm", tm4);

            layerL2.HtmModules.Add("sp", sp2);
            layerL2.HtmModules.Add("tm", tm2);

            int[] inpCellsL4ToL2 = new int[cfgL4.CellsPerColumn * cfgL4.NumColumns];

            double[] inputs = inputValues.ToArray();
            int[] prevActiveCols = new int[0];
            int cycle = 0;
            int matches = 0;
            string lastPredictedValue = "0";
            int maxCycles = 3500;
            int maxPrevInputs = inputValues.Count - 1;
            List<string> previousInputs = new List<string>();

            //
            // Training SP at Layer 4 to get stable. New-born stage.
            //

            using (StreamWriter swL4Sdrs = new StreamWriter($"L4-SDRs-in_{cfgL2.NumInputs}-col_{cfgL2.NumColumns}-r_{cfgL2.PotentialRadius}.txt"))
            {
                using (StreamWriter sw = new StreamWriter($"in_{cfgL2.NumInputs}-col_{cfgL2.NumColumns}-r_{cfgL2.PotentialRadius}.txt"))
                {
                    for (int i = 0; i < maxCycles; i++)
                    {
                        matches = 0;
                        cycle=i;
                        Debug.WriteLine($"-------------- Newborn Cycle {cycle} at L4 SP region  ---------------");

                        foreach (var input in inputs)
                        {
                            Debug.WriteLine($" INPUT: '{input}'\tCycle:{cycle}");
                            Debug.Write("L4: ");
                            var lyrOut = layerL4.Compute(input, learn);

                            /// <summary>
                            /// This part is for to make SP of Layer2 stable thourgh help of HPC.  
                            /// We skip this part right now to check how it works at uppler layer 2 without stability
                            /// but if you want experiment this just
                            /// uncomment the code part area from Line 212 to 231 and 236 to 237
                            /// & then comment out Line 238 to 239
                            /// </summary>

                            //InitArray(inpCellsL4ToL2, 0);
                            // if (isSP4Stable)
                            //{
                            // var cellSdrL4Indexes = memL4.ActiveCells.Select(c => c.Index).ToArray();
                            /// Write SDR as output of L4 and input of L2
                            // swL4Sdrs.WriteLine($"{input} - {Helpers.StringifyVector(cellSdrL4Indexes)}");
                            // Set the output active cell array
                            // ArrayUtils.SetIndexesTo(inpCellsL4ToL2, cellSdrL4Indexes, 1);
                            // Debug.WriteLine($"L4 out sdr: {Helpers.StringifyVector(cellSdrL4Indexes)}");
                            // Debug.WriteLine("L2: ");
                            // swL2.Restart();
                            // layerL2.Compute(inpCellsL4ToL2, true);
                            // swL2.Stop();
                            // Debug.WriteLine($"{swL2.ElapsedMilliseconds / 1000}");
                            // sw.WriteLine($"{swL2.ElapsedMilliseconds / 1000}");
                            // sw.Flush();
                            // var overlaps = ArrayUtils.IndexWhere(memL2.Overlaps, o => o > 0);
                            // var strOverlaps = Helpers.StringifyVector(overlaps);
                            // Debug.WriteLine($"Potential columns: {overlaps.Length}, overlaps: {strOverlaps}");
                            //}


                        }

                        //if (isSP4Stable && isSP2STable)
                        // break;
                        if (isSP4Stable)
                            break;
                    }
                }
            }

            Debug.WriteLine($"-------------- L4 SP region is  {isSP4Stable} ---------------");

            //layerL4.HtmModules.Add("tm", tm4);


            // SP+TM at L4

            for (int i = 0; i < maxCycles; i++)
            {
                matches = 0;

                cycle = i;

                Debug.WriteLine($"-------------- L4 TM Train region Cycle {cycle} ---------------");

                foreach (var input in inputs)
                {
                    Debug.WriteLine($"-------------- {input} ---------------");

                    var layerL4Out = layerL4.Compute(input, learn) as ComputeCycle;

                    previousInputs.Add(input.ToString());
                    if (previousInputs.Count > (maxPrevInputs + 1))
                        previousInputs.RemoveAt(0);

                    if (previousInputs.Count < maxPrevInputs)
                        continue;
                    string key = GetKey(previousInputs, input);
                    List<Cell> actCells;

                    if (layerL4Out.ActiveCells.Count == layerL4Out.WinnerCells.Count)
                    {
                        // SP+TM at L2

                        Debug.WriteLine($"-------------- L2 TM Train region Cycle {cycle} ---------------");
                        // Reset tha array
                        InitArray(inpCellsL4ToL2, 0);
                        var cellSdrL4Indexes = memL4.ActiveCells.Select(c => c.Index).ToArray();

                        // Set the output active cell array
                        ArrayUtils.SetIndexesTo(inpCellsL4ToL2, cellSdrL4Indexes, 1);
                        var layerL2Out = layerL2.Compute(inpCellsL4ToL2, true) as ComputeCycle;
                        var overlaps = ArrayUtils.IndexWhere(memL2.Overlaps, o => o > 0);
                        var strOverlaps = Helpers.StringifyVector(overlaps);
                        Debug.WriteLine($"Potential columns: {overlaps.Length}, overlaps: {strOverlaps}");

                        if (layerL2Out.ActiveCells.Count == layerL2Out.WinnerCells.Count)
                        {
                            actCells = layerL2Out.ActiveCells;
                        }
                        else
                        {
                            actCells = layerL2Out.WinnerCells;
                        }

                        cls.Learn(key, actCells.ToArray());


                        if (key == lastPredictedValue)
                        {
                            matches++;
                            Debug.WriteLine($"Match. Actual value: {key} - Predicted value: {lastPredictedValue}");
                        }
                        else
                            Debug.WriteLine($"Missmatch! Actual value: {key} - Predicted value: {lastPredictedValue}");

                        if (layerL2Out.PredictiveCells.Count > 0)
                        {
                            var predictedInputValue = cls.GetPredictedInputValue(layerL2Out.PredictiveCells.ToArray());

                            Debug.WriteLine($"Current Input: {input} \t| Predicted Input: {predictedInputValue}");

                            lastPredictedValue = predictedInputValue;
                        }
                        else
                        {
                            Debug.WriteLine($"NO CELLS PREDICTED for next cycle.");
                            lastPredictedValue = String.Empty;
                        }



                    }


                }

                double accuracy = (double)matches / (double)inputs.Length * 100.0;

                Debug.WriteLine($"Cycle: {cycle}\tMatches={matches} of {inputs.Length}\t {accuracy}%");

                if (accuracy >= 100.0)
                {
                    maxMatchCnt++;
                    Debug.WriteLine($"100% accuracy reched {maxMatchCnt} times.");
                    //
                    // Experiment is completed if we are 20 cycles long at the 100% accuracy.
                    if (maxMatchCnt >= 20)
                    {

                        Debug.WriteLine($"Exit experiment in the stable state after 20 repeats with 100% of accuracy.");
                        learn = false;
                        break;
                    }
                }
                else if (maxMatchCnt > 0)
                {
                    Debug.WriteLine($"At 100% accuracy after {maxMatchCnt} repeats we get a drop of accuracy with {accuracy}. This indicates instable state. Learning will be continued.");
                    maxMatchCnt = 0;
                }
            }

        }

        private static void InitArray(int[] array, int val)
        {
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = val;
            }
        }

        private static string GetKey(List<string> prevInputs, double input)
        {
            string key = String.Empty;

            for (int i = 0; i < prevInputs.Count; i++)
            {
                if (i > 0)
                    key += "-";

                key += (prevInputs[i]);
            }

            return key;
        }
    }
}
