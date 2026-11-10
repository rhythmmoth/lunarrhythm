using System;
using YARG.Core;
using YARG.Core.Input;

namespace YARG.Input
{
    public partial class BindingCollection
    {
        public static BindingCollection CreateMenuBindings() => new(null)
        {
            new ButtonBinding("Menu.Start",  (int) MenuAction.Start),
            new ButtonBinding("Menu.Select", (int) MenuAction.Select),

            new ButtonBinding("Menu.Green",  (int) MenuAction.Green),
            new ButtonBinding("Menu.Red",    (int) MenuAction.Red),
            new ButtonBinding("Menu.Yellow", (int) MenuAction.Yellow),
            new ButtonBinding("Menu.Blue",   (int) MenuAction.Blue),
            new ButtonBinding("Menu.Orange", (int) MenuAction.Orange),

            new ButtonBinding("Menu.Up",    (int) MenuAction.Up),
            new ButtonBinding("Menu.Down",  (int) MenuAction.Down),
            new ButtonBinding("Menu.Left",  (int) MenuAction.Left),
            new ButtonBinding("Menu.Right", (int) MenuAction.Right),
        };

        public static BindingCollection CreateFiveFretGuitarBindings() => new(GameMode.FiveFretGuitar)
        {
            new ButtonBinding("FiveFret.Green",  (int) GuitarAction.GreenFret),
            new ButtonBinding("FiveFret.Red",    (int) GuitarAction.RedFret),
            new ButtonBinding("FiveFret.Yellow", (int) GuitarAction.YellowFret),
            new ButtonBinding("FiveFret.Blue",   (int) GuitarAction.BlueFret),
            new ButtonBinding("FiveFret.Orange", (int) GuitarAction.OrangeFret),
            // Also need bindings for solo frets
            new ButtonBinding("FiveFret.SoloGreen", (int) GuitarAction.SoloGreenFret),
            new ButtonBinding("FiveFret.SoloRed",   (int) GuitarAction.SoloRedFret),
            new ButtonBinding("FiveFret.SoloYellow",(int) GuitarAction.SoloYellowFret),
            new ButtonBinding("FiveFret.SoloBlue",  (int) GuitarAction.SoloBlueFret),
            new ButtonBinding("FiveFret.SoloOrange",(int) GuitarAction.SoloOrangeFret),

            new ButtonBinding("Guitar.StrumUp",   (int) GuitarAction.StrumUp),
            new ButtonBinding("Guitar.StrumDown", (int) GuitarAction.StrumDown),

            new IndividualButtonBinding("Guitar.StarPower", (int) GuitarAction.StarPower),

            new AxisBinding("Guitar.Whammy", (int) GuitarAction.Whammy),
        };

        public static BindingCollection CreateSixFretGuitarBindings() => new(GameMode.SixFretGuitar)
        {
            new ButtonBinding("SixFret.Black1", (int) GuitarAction.Black1Fret),
            new ButtonBinding("SixFret.Black2", (int) GuitarAction.Black2Fret),
            new ButtonBinding("SixFret.Black3", (int) GuitarAction.Black3Fret),
            new ButtonBinding("SixFret.White1", (int) GuitarAction.White1Fret),
            new ButtonBinding("SixFret.White2", (int) GuitarAction.White2Fret),
            new ButtonBinding("SixFret.White3", (int) GuitarAction.White3Fret),

            new ButtonBinding("Guitar.StrumUp",   (int) GuitarAction.StrumUp),
            new ButtonBinding("Guitar.StrumDown", (int) GuitarAction.StrumDown),

            new IndividualButtonBinding("Guitar.StarPower", (int) GuitarAction.StarPower),

            new AxisBinding("Guitar.Whammy", (int) GuitarAction.Whammy),
        };

        public static BindingCollection CreateFourLaneDrumsBindings() => new(GameMode.FourLaneDrums)
        {
            new DrumPadButtonBinding("FourDrums.RedPad",    (int) DrumsAction.RedDrum),
            new DrumPadButtonBinding("FourDrums.YellowPad", (int) DrumsAction.YellowDrum),
            new DrumPadButtonBinding("FourDrums.BluePad",   (int) DrumsAction.BlueDrum),
            new DrumPadButtonBinding("FourDrums.GreenPad",  (int) DrumsAction.GreenDrum),

            new DrumPadButtonBinding("FourDrums.YellowCymbal", (int) DrumsAction.YellowCymbal),
            new DrumPadButtonBinding("FourDrums.BlueCymbal",   (int) DrumsAction.BlueCymbal),
            new DrumPadButtonBinding("FourDrums.GreenCymbal", "FourDrums.RedCymbal", (int) DrumsAction.GreenCymbal),

            new DrumPadButtonBinding("Drums.Kick", (int) DrumsAction.Kick),
        };

        public static BindingCollection CreateFiveLaneDrumsBindings() => new(GameMode.FiveLaneDrums)
        {
            new DrumPadButtonBinding("FiveDrums.RedPad",       (int) DrumsAction.RedDrum),
            new DrumPadButtonBinding("FiveDrums.YellowCymbal", (int) DrumsAction.YellowCymbal),
            new DrumPadButtonBinding("FiveDrums.BluePad",      (int) DrumsAction.BlueDrum),
            new DrumPadButtonBinding("FiveDrums.OrangeCymbal", (int) DrumsAction.OrangeCymbal),
            new DrumPadButtonBinding("FiveDrums.GreenPad",     (int) DrumsAction.GreenDrum),

            new DrumPadButtonBinding("Drums.Kick", (int) DrumsAction.Kick),
        };

        public static BindingCollection CreateEliteDrumsBindings() => new(GameMode.EliteDrums)
        {
            new DrumPadButtonBinding("Drums.Kick", (int) EliteDrumsAction.Kick),

            /*
            new DrumPadButtonBinding("EliteDrums.Stomp", (int) EliteDrumsAction.EliteStomp),
            new DrumPadButtonBinding("EliteDrums.Splash", (int) EliteDrumsAction.EliteSplash),
            new DrumPadButtonBinding("EliteDrums.Snare", (int) EliteDrumsAction.EliteSnare),
            new DrumPadButtonBinding("EliteDrums.ClosedHiHat", (int) EliteDrumsAction.EliteClosedHiHat),
            new DrumPadButtonBinding("EliteDrums.SizzleHiHat", (int) EliteDrumsAction.EliteSizzleHiHat),
            new DrumPadButtonBinding("EliteDrums.OpenHiHat", (int) EliteDrumsAction.EliteOpenHiHat),
            new DrumPadButtonBinding("EliteDrums.LeftCrash", (int) EliteDrumsAction.EliteLeftCrash),
            new DrumPadButtonBinding("EliteDrums.Tom1", (int) EliteDrumsAction.EliteTom1),
            new DrumPadButtonBinding("EliteDrums.Tom2", (int) EliteDrumsAction.EliteTom2),
            new DrumPadButtonBinding("EliteDrums.Tom3", (int) EliteDrumsAction.EliteTom3),
            new DrumPadButtonBinding("EliteDrums.Ride", (int) EliteDrumsAction.EliteRide),
            new DrumPadButtonBinding("EliteDrums.RightCrash", (int) EliteDrumsAction.EliteRightCrash),
            */

            new DrumPadButtonBinding("EliteDrums.FourLaneRedDrum", (int) EliteDrumsAction.FourLaneRedDrum),
            new DrumPadButtonBinding("EliteDrums.FourLaneYellowDrum", (int) EliteDrumsAction.FourLaneYellowDrum),
            new DrumPadButtonBinding("EliteDrums.FourLaneBlueDrum", (int) EliteDrumsAction.FourLaneBlueDrum),
            new DrumPadButtonBinding("EliteDrums.FourLaneGreenDrum", (int) EliteDrumsAction.FourLaneGreenDrum),
            new DrumPadButtonBinding("EliteDrums.FourLaneYellowCymbal", (int) EliteDrumsAction.FourLaneYellowCymbal),
            new DrumPadButtonBinding("EliteDrums.FourLaneBlueCymbal", (int) EliteDrumsAction.FourLaneBlueCymbal),
            new DrumPadButtonBinding("EliteDrums.FourLaneGreenCymbal", (int) EliteDrumsAction.FourLaneGreenCymbal),
                                      
            new DrumPadButtonBinding("EliteDrums.FiveLaneRedDrum", (int) EliteDrumsAction.FiveLaneRedDrum),
            new DrumPadButtonBinding("EliteDrums.FiveLaneBlueDrum", (int) EliteDrumsAction.FiveLaneBlueDrum),
            new DrumPadButtonBinding("EliteDrums.FiveLaneGreenDrum", (int) EliteDrumsAction.FiveLaneGreenDrum),
            new DrumPadButtonBinding("EliteDrums.FiveLaneYellowCymbal", (int) EliteDrumsAction.FiveLaneYellowCymbal),
            new DrumPadButtonBinding("EliteDrums.FiveLaneOrangeCymbal", (int) EliteDrumsAction.FiveLaneOrangeCymbal)
        };

        public static BindingCollection CreateProGuitarBindings() => new(GameMode.ProGuitar)
        {
            new IntegerBinding("ProGuitar.String1_Fret", (int) ProGuitarAction.String1_Fret),
            new IntegerBinding("ProGuitar.String2_Fret", (int) ProGuitarAction.String2_Fret),
            new IntegerBinding("ProGuitar.String3_Fret", (int) ProGuitarAction.String3_Fret),
            new IntegerBinding("ProGuitar.String4_Fret", (int) ProGuitarAction.String4_Fret),
            new IntegerBinding("ProGuitar.String5_Fret", (int) ProGuitarAction.String5_Fret),
            new IntegerBinding("ProGuitar.String6_Fret", (int) ProGuitarAction.String6_Fret),

            new ButtonBinding("ProGuitar.String1_Strum", (int) ProGuitarAction.String1_Strum),
            new ButtonBinding("ProGuitar.String2_Strum", (int) ProGuitarAction.String2_Strum),
            new ButtonBinding("ProGuitar.String3_Strum", (int) ProGuitarAction.String3_Strum),
            new ButtonBinding("ProGuitar.String4_Strum", (int) ProGuitarAction.String4_Strum),
            new ButtonBinding("ProGuitar.String5_Strum", (int) ProGuitarAction.String5_Strum),
            new ButtonBinding("ProGuitar.String6_Strum", (int) ProGuitarAction.String6_Strum),

            new IndividualButtonBinding("Guitar.StarPower", (int) ProGuitarAction.StarPower),
        };

        public static BindingCollection CreateProKeysBindings() => new(GameMode.ProKeys)
        {
            // Pad game: only the five lanes and Overdrive
            new ButtonBinding("ProKeys.GreenKey", (int) ProKeysAction.GreenKey),
            new ButtonBinding("ProKeys.RedKey", (int) ProKeysAction.RedKey),
            new ButtonBinding("ProKeys.YellowKey", (int) ProKeysAction.YellowKey),
            new ButtonBinding("ProKeys.BlueKey", (int) ProKeysAction.BlueKey),
            new ButtonBinding("ProKeys.OrangeKey", (int) ProKeysAction.OrangeKey),

            new IndividualButtonBinding("ProKeys.StarPower", (int) ProKeysAction.StarPower),
        };

        public static BindingCollection CreateVocalsBindings() => new(GameMode.Vocals)
        {
            // Only needed if we want to over-do it and run audio devices
            // through the Unity input system (which actually wouldn't be that hard lol)
            // new AxisBinding("Vocals.Pitch", (int) VocalsAction.Pitch),

            new IndividualButtonBinding("Vocals.Hit", (int) VocalsAction.Hit),
            new IndividualButtonBinding("Vocals.StarPower", (int) VocalsAction.StarPower)
        };

        public static BindingCollection CreateGameplayBindings(GameMode mode)
        {
            return mode switch
            {
                GameMode.FiveFretGuitar => CreateFiveFretGuitarBindings(),
                GameMode.SixFretGuitar => CreateSixFretGuitarBindings(),

                GameMode.FourLaneDrums => CreateFourLaneDrumsBindings(),
                GameMode.FiveLaneDrums => CreateFiveLaneDrumsBindings(),
                GameMode.EliteDrums => CreateEliteDrumsBindings(),

                GameMode.ProGuitar => CreateProGuitarBindings(),
                GameMode.ProKeys => CreateProKeysBindings(),

                GameMode.Vocals => CreateVocalsBindings(),

                _ => throw new NotImplementedException($"Unhandled game mode {mode}!")
            };
        }
    }
}