using System;
using System.Diagnostics;
using ObscureWare.ModernD20.EffectBuilders;

namespace ObscureWare.ModernD20.Engine
{
    [Serializable] // TODO: register specialzied serialziers with effect hierrarchy
    public abstract class AppliedCharacterEffect
    {
        protected AppliedCharacterEffect(BaseEffectBuilder effectBuilder)
        {
            this.EffectId = effectBuilder.Id;
            this.RemovedMessage = effectBuilder.GetRemovedMessage();
        }

        public string RemovedMessage { get; set; }

        public Guid EffectId { get; private set; }

        // TODO: more props like whether is temporary or constant, where it started, link to source (if external) / feat item, when it ends...
        // TODO: below some proposals, but this is not finished...



        public void AddExtraEfect(AppliedCharacterEffect efefctBuilder)
        {
            throw new NotImplementedException();
        }

        public int Value { get; set; }

        public bool ReplacePrevious { get; set; } // or stacks?

        public long StartRound { get; set; }

        public long LastUpdateRound { get; set; }

        public int? ApplyForManyRounds { get; set; } // null - > infinite?


        public virtual void StackWith(AppliedCharacterEffect effect) // possibly virtual
        {
            Debug.Assert(this.EffectId == effect.EffectId);

            this.Value += effect.Value;
            // TODO: stack infinity or time...
        }

        ///// <summary>
        ///// Update next round of Effect (mostly during fight)
        ///// </summary>
        ///// <param name="character"></param>
        ///// <param name="environment"></param>
        ///// <returns>TRUE, when effect had finished and might be removed from character.</returns>
        //public bool UpdateNextEffectRound(Character character, ModernD20Library environment)
        //{
        //    //if (IsInfinite)
        //    //{
        //    //    return false;
        //    //}

        //    LastUpdateRound++;

        //    return (LastUpdateRound - StartRound > ApplyForManyRounds);
        //}

        

        // TODO: next method to update longer periods - like days, weeks, to simulate away characters  - non frequent

        public bool IsInfinite
        {
            get { return this.ApplyForManyRounds == null; } // temporary
        }

        #region Updating

        protected virtual void UpdateRound(ModernD20Library environment, Character character)
        {
            // TODO: if finite time - just lower remaining rounds

            throw new NotImplementedException();
        }

        protected virtual void UpdateWithRest(ModernD20Library environment, Character character, RestTypeEnum restType)
        {
            // nothing in base class
        }

        #endregion
    }
}