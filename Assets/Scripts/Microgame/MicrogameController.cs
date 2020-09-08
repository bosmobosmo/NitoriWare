﻿using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using System.Collections;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MicrogameController : MonoBehaviour
{
	public static MicrogameController instance;
	private static int preserveDebugSpeed = -1;
    private static int langaugeCycleIndex = 0;
    private static MicrogameSession forceDebugSession;

	[SerializeField]
	private DebugSettings debugSettings;
	[System.Serializable]
	struct DebugSettings
	{
		public bool playMusic, displayCommand, showTimer, timerTick, simulateStartDelay, localizeText;
        public string forceLocalizationLanguage;
        public bool resetThroughAllLanguages;
		public VoicePlayer.VoiceSet voiceSet;
		[Range(1, StageController.MAX_SPEED)]
		public int speed;
        [Header("For microgames where difficulty isn't dependent on scene:")]
        public DebugDifficulty SimulateDifficulty;
    }

    [SerializeField]
    private DebugKeys debugKeys;
    [System.Serializable]
    class DebugKeys
    {
        public KeyCode Restart = KeyCode.R;
        public KeyCode Faster = KeyCode.F;
        public KeyCode NextDifficulty = KeyCode.N;
        public KeyCode PreviousDifficulty = KeyCode.M;
    }

    public enum DebugDifficulty
    {
        Default,
        Stage1,
        Stage2,
        Stage3
    }


	public UnityEvent onPause, onUnPause;
    [Header("--NOTE--")]
    [Header("Please don't touch anything below here in this GameObject.")]
    [Header("--------")]

    [SerializeField]
    private AudioSource sfxSource;

	private bool victory, victoryDetermined;
    private bool debugMode;
    private CommandDisplay commandDisplay;

    private MicrogameCollection.Microgame microgameData;
    private MicrogameTraits traits => microgameData.traits;
    public MicrogameSession Session { get; private set; }
    public int Difficulty => Session.Difficulty;

    void Awake()
	{
		instance = this;

        var sceneName = gameObject.scene.name;
        if (sceneName.Contains("Template"))
        {
            Debug.Break();
            Debug.Log("You can't play the template scene, copy the folder and rename the scene so it contains your microgame's ID");
        }

        // Get collection microgame if available
        microgameData = MicrogameHelper.getMicrogames(includeBosses:true)
            .FirstOrDefault(a =>  a.microgameId.Equals(sceneName));

        // Otherwise create collection microgame
        if (microgameData == null)
        {
#if UNITY_EDITOR
            microgameData = MicrogameCollection.instance.createMicrogameForScene(gameObject.scene.name);
#else
            Debug.LogError("Failed to find microgame for " + gameObject.scene.name);
#endif
        }

        if (microgameData == null)
        {
            Debug.Break();
            Debug.Log("Can't ascertain microgame ID. Make sure scene name contains Microgame ID and the folder is named correctly.");
        }
        else if (microgameData.traits == null)
        {
            Debug.Break();
            Debug.Log("Can't find microgame traits asset. Make sure it's in the root folder of your microgame and named correctly.");
        }

        debugMode = GameController.instance == null || GameController.instance.getStartScene() == "Microgame Debug";

        if (debugMode)
		{
            //Debug Mode Awake (scene open by itself)

            if (MicrogameDebugObjects.instance == null)
                SceneManager.LoadScene("Microgame Debug", LoadSceneMode.Additive);
            else
                MicrogameDebugObjects.instance.Reset();

            if (forceDebugSession != null)
            {
                Session = forceDebugSession;
            }
            else
            {
                int difficulty;
                if (traits.SceneDeterminesDifficulty)
                    difficulty = traits.GetDifficultyFromScene(gameObject.scene.name);
                else
                    difficulty = debugSettings.SimulateDifficulty > 0 ? (int)debugSettings.SimulateDifficulty : 1;
                Session = traits.onAccessInStage(microgameData.microgameId, difficulty, debugMode: true);
            }


            if (preserveDebugSpeed > -1)
            {
                debugSettings.speed = preserveDebugSpeed;
                preserveDebugSpeed = -1;
            }

            StageController.beatLength = 60f / 130f;
            Time.timeScale = StageController.getSpeedMult(debugSettings.speed);

            victory = traits.defaultVictory;
            victoryDetermined = false;
        }
		else if (!isBeingDiscarded())
		{
			//Normal Awake

			StageController.instance.stageCamera.tag = "Camera";
            //Camera.main.GetComponent<AudioListener>().enabled = false;

            Session = StageController.instance.CurrentMicrogameSession;

			StageController.instance.microgameMusicSource.clip = traits.GetMusicClip(Session);

			if (traits.GetHideCursor(Session))
				Cursor.visible = false;

			commandDisplay = StageController.instance.transform.root.Find("UI").Find("Command").GetComponent<CommandDisplay>();

			StageController.instance.resetVictory();
			StageController.instance.onMicrogameAwake();
		}

	}

	void Start()
	{
		if (isBeingDiscarded())
			shutDownMicrogame();
		else
        {
            if (debugMode)
            {
                //Debug Start
                MicrogameDebugObjects debugObjects  = MicrogameDebugObjects.instance;
                commandDisplay = debugObjects.commandDisplay;
                
                if (debugSettings.localizeText)
                {
                    LocalizationManager manager = GameController.instance.transform.Find("Localization").GetComponent<LocalizationManager>();
                    if (!string.IsNullOrEmpty(debugSettings.forceLocalizationLanguage))
                        manager.setForcedLanguage(debugSettings.forceLocalizationLanguage);
                    else if (debugSettings.resetThroughAllLanguages)
                    {
                        var languages = LanguagesData.instance.languages;
                        var currentLanguageName = languages[langaugeCycleIndex++].getLanguageID();
                        if (LocalizationManager.instance != null)
                            manager.setLanguage(currentLanguageName);
                        else
                            manager.setForcedLanguage(currentLanguageName);
                        if (langaugeCycleIndex >= languages.Count())
                            langaugeCycleIndex = 0;
                        print("Language cycling debugging in " + currentLanguageName);
                    }
                    manager.gameObject.SetActive(true);
                }
                
                MicrogameTimer.instance.beatsLeft = (float)traits.getDurationInBeats() + (debugSettings.simulateStartDelay ? 1f : 0f);
                if (!debugSettings.showTimer)
                    MicrogameTimer.instance.disableDisplay = true;
                if (debugSettings.timerTick)
                    MicrogameTimer.instance.invokeTick();

                var musicClip = traits.GetMusicClip(Session);
                if (debugSettings.playMusic && musicClip != null)
                {
                    AudioSource source = debugObjects.musicSource;
                    source.clip = musicClip;
                    source.pitch = StageController.getSpeedMult(debugSettings.speed);
                    if (!debugSettings.simulateStartDelay)
                        source.Play();
                    else
                        AudioHelper.playScheduled(source, StageController.beatLength);
                }
                
                if (debugSettings.displayCommand)
                debugObjects.commandDisplay.play(traits.GetLocalizedCommand(Session), traits.GetCommandAnimatorOverride(Session));

                Cursor.visible = traits.controlScheme == MicrogameTraits.ControlScheme.Mouse && !traits.GetHideCursor(Session);
                Cursor.lockState = getTraits().GetCursorLockState(Session);
                //Cursor.lockState = CursorLockMode.Confined;

                debugObjects.voicePlayer.loadClips(debugSettings.voiceSet);

            }
            SceneManager.SetActiveScene(gameObject.scene);
        }
	}

    public void onPaused()
    {
        onPause.Invoke();
    }

    public void onUnPaused()
    {
        onUnPause.Invoke();
    }

	/// <summary>
	/// Disables all root objects in microgame
	/// </summary>
	public void shutDownMicrogame()
	{
		GameObject[] rootObjects = gameObject.scene.GetRootGameObjects();
        foreach (var rootObject in rootObjects)
        {
            rootObject.SetActive(false);

            //Is there a better way to do this?
            var monobehaviours = rootObject.GetComponentsInChildren<MonoBehaviour>();
            foreach (var behaviour in monobehaviours)
            {
                behaviour.CancelInvoke();
            }
        }
	}

	bool isBeingDiscarded()
	{
        if (debugMode)
            return false;
		return StageController.instance == null
            || StageController.instance.animationPart == StageController.AnimationPart.GameOver
            || StageController.instance.animationPart == StageController.AnimationPart.WonStage
            || PauseManager.exitedWhilePaused;
	}

	/// <summary>
	/// Returns MicrogameTraits for the microgame (from the prefab)
	/// </summary>
	/// <returns></returns>
	public MicrogameTraits getTraits()
	{
		return traits;
	}

	/// <summary>
	/// Returns true if microgame is in debug mode (scene open by itself)
	/// </summary>
	/// <returns></returns>
	public bool isDebugMode()
	{
        return debugMode;
	}

    string getSceneWithoutNumber(string scene)
    {
        return scene.Substring(0, scene.Length - 1);
    }
    
    public AudioSource getSFXSource()
    {
        return sfxSource;
    }

    /// <summary>
    /// Call this to have the player win/lose a microgame. If victory status may change before the end of the microgame, add a second "false" bool parameter
    /// </summary>
    /// <param name="victory"></param>
    /// <param name="final"></param>
    public void setVictory(bool victory)
    {
        setVictory(victory, true);
    }

    /// <summary>
    /// Call this to have the player win/lose a microgame, set 'final' to false if the victory status might be changed again before the microgame is up
    /// </summary>
    /// <param name="victory"></param>
    /// <param name="final"></param>
    public void setVictory(bool victory, bool final)
	{
		if (debugMode)
		{
			//Debug victory
			if (victoryDetermined)
			{
				return;
			}
			this.victory = victory;
			victoryDetermined = final;
			if (final)
				MicrogameDebugObjects.instance.voicePlayer.playClip(victory, victory
                    ? traits.GetVictoryVoiceDelay(Session)
                    : traits.GetFailureVoiceDelay(Session));
		}
		else
		{
			//StageController handles regular victory
			StageController.instance.setMicrogameVictory(victory, final);
		}
	}
	
	/// <summary>
	/// Returns whether the game would be won if it ends now
	/// </summary>
	/// <returns></returns>
	public bool getVictory()
	{
		if (debugMode)
		{
			return victory;
		}
		else
			return StageController.instance.getMicrogameVictory();
	}

	/// <summary>
	/// Returns true if the game's victory outcome will not be changed for the rest of its duration
	/// </summary>
	/// <returns></returns>
	public bool getVictoryDetermined()
	{
		if (debugMode)
		{
			return victoryDetermined;
		}
		else
			return StageController.instance.getVictoryDetermined();
	}

	/// <summary>
	/// Re-displays the command text with the specified message. Only use this if the text will not need to be localized
	/// </summary>
	/// <param name="command"></param>
	public void displayCommand(string command, AnimatorOverrideController commandAnimatorOverride = null)
	{
		if (!commandDisplay.gameObject.activeInHierarchy)
			commandDisplay.gameObject.SetActive(true);


        commandDisplay.play(command, commandAnimatorOverride);
	}

    /// <summary>
    /// Gets the currently active command display
    /// </summary>
    /// <returns></returns>
    public CommandDisplay getCommandDisplay()
    {
        return commandDisplay;
    }

	/// <summary>
	/// Re-displays the command text with a localized message. Key is automatically prefixed with "microgame.[ID]."
	/// </summary>
	/// <param name="command"></param>
	public void displayLocalizedCommand(string key, string defaultString, AnimatorOverrideController commandAnimatorOverride = null)
	{
		displayCommand(TextHelper.getLocalizedMicrogameText(key, defaultString), commandAnimatorOverride);
	}

    /// <summary>
    /// Plays sound effect unaffected by microgame speed
    /// </summary>
    /// <param name="clip"></param>
    /// <param name="panStereo"></param>
    /// <param name="pitch"></param>
    /// <param name="volume"></param>
    public void playSFXUnscaled(AudioClip clip, float panStereo = 0f, float pitch = 1f, float volume = 1f)
    {
        sfxSource.pitch = pitch;
        sfxSource.panStereo = panStereo;
        sfxSource.PlayOneShot(clip, volume * PrefsHelper.getVolume(PrefsHelper.VolumeType.SFX));
    }

    /// <summary>
    /// Plays sound effect and scales it with current speed. use this for most microgame sounds.
    /// </summary>
    /// <param name="clip"></param>
    /// <param name="panStereo"></param>
    /// <param name="pitchMult"></param>
    /// <param name="volume"></param>
    public void playSFX(AudioClip clip, float panStereo = 0f, float pitchMult = 1f, float volume = 1f)
    {
        playSFXUnscaled(clip, panStereo, pitchMult * Time.timeScale, volume);
    }

	void Update ()
	{
		if (debugMode)
		{
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKeyDown(debugKeys.Restart))
                {
                    forceDebugSession = traits.onAccessInStage(Session.MicrogameId, Session.Difficulty, debugMode: true);
                    SceneManager.LoadScene(traits.GetSceneName(forceDebugSession));
                }
                else if (Input.GetKeyDown(debugKeys.Faster))
                {
                    forceDebugSession = traits.onAccessInStage(Session.MicrogameId, Session.Difficulty, debugMode: true);
                    preserveDebugSpeed = Mathf.Min(debugSettings.speed + 1, StageController.MAX_SPEED);
                    Debug.Log("Debugging at speed " + preserveDebugSpeed);
                    SceneManager.LoadScene(traits.GetSceneName(forceDebugSession));
                }
                else if (Input.GetKeyDown(debugKeys.NextDifficulty))
                {
                    forceDebugSession = traits.onAccessInStage(Session.MicrogameId, Mathf.Min(Session.Difficulty + 1, 3), debugMode: true);
                    Debug.Log("Debugging at difficulty " + forceDebugSession.Difficulty);
                    SceneManager.LoadScene(traits.GetSceneName(forceDebugSession));
                }
                else if (Input.GetKeyDown(debugKeys.PreviousDifficulty))
                {
                    forceDebugSession = traits.onAccessInStage(Session.MicrogameId, Mathf.Max(Session.Difficulty - 1, 1), debugMode: true);
                    Debug.Log("Debugging at difficulty " + forceDebugSession.Difficulty);
                    SceneManager.LoadScene(traits.GetSceneName(forceDebugSession));
                }
            }
        }
	}
}
