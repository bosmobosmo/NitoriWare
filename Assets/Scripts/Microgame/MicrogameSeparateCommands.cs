﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Microgame/Separate Commands")]
public class MicrogameSeparateCommands : Microgame
{
    public string commandKeySuffix;

    [SerializeField]
    private DifficultyCommand difficulty1;
    [SerializeField]
    private DifficultyCommand difficulty2;
    [SerializeField]
    private DifficultyCommand difficulty3;

    [System.Serializable]
    public class DifficultyCommand
    {
        public string commandKeySuffix;
        public string defaultValue;
    }

    class Session : MicrogameSession
    {
        private DifficultyCommand command;

        public override string GetLocalizedCommand()
            => TextHelper.getLocalizedText("microgame." + microgame.microgameId + ".command" + command.commandKeySuffix,
                command.defaultValue);

        public Session(Microgame microgame, StageController player, int difficulty, bool debugMode, DifficultyCommand command)
            : base(microgame, player, difficulty, debugMode)
        {
            this.command = command;
        }
    }
    
    public override MicrogameSession CreateSession(StageController player, int difficulty, bool debugMode = false)
    {
        return new Session(this, player, difficulty, debugMode, GetCommand(difficulty));
    }

    private DifficultyCommand GetCommand(int difficulty)
    {
        switch (difficulty)
        {
            case (1):
                return difficulty1;
            case (2):
                return difficulty2;
            case (3):
                return difficulty3;
        }
        return null;
    }
}