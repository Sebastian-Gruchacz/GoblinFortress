using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ObscureWare.D20Common;
using ObscureWare.D20Common.Generators;

namespace Obscureware.D20Common.Tests
{
    [TestClass]
    public class AbilityScoreGeneratorsTests
    {

        // TODO: use MEF containers - like will be used by engine itself
        private readonly IAbilityScoresGenerator[] _knownGenerators = new IAbilityScoresGenerator[]
        {
            new ClassicAbilityScoresGenerator(), 
            new DicePoolAbilityScoresGenerator(false), 
            new DicePoolAbilityScoresGenerator(true), 
            new HeroicAbilityScoresGenerator(), 
            new StandardAbilityScoresGenerator(), 
        };

        // Note these test are just pure random, so I'm not going to test scores, rather the stability of execution

        private readonly IRoller _testRoller = new DefaultRoller(new DefaultRandomizer());

        [TestMethod]
        public void TestWhetherAllGeneratorsReturnExpectedSizeOfArray()
        {
            int expectedSize = 6; // Enum.GetValues(typeof (AbilityEnum)).Length;

            foreach (var generator in _knownGenerators)
            {
                var testScore = generator.GenerateScores(_testRoller).ToArray();
                PrintScore(generator.GetType().Name, testScore);

                Assert.AreEqual(expectedSize, testScore.Length);
            }
        }

        private void PrintScore(string generatorName, uint[] score)
        {
            var scoreString = String.Join(", ", score.Select(s => s.ToString()));

            Console.WriteLine($"Generator {generatorName} scores: {scoreString}");
        }
    }
}
