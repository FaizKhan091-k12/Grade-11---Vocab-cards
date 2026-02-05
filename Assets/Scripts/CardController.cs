using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(CanvasGroup))]
public class CardController : MonoBehaviour,
    IPointerClickHandler,
    IPointerEnterHandler,
    IPointerExitHandler
{
    // ---------- Registry so we can cancel pending audio across all cards ----------
    static readonly List<CardController> allCards = new List<CardController>();

    // ---------- GLOBAL HOVER AUDIO LOCK ----------
    static AudioSource currentlyPlayingHoverSource = null;

    [Header("FRONT")]
    public TextMeshProUGUI frontKeyword;

    [Header("BACK")]
    public GameObject backSide;
    public Image backImage;
    public TextMeshProUGUI backInfo;

    [Header("Flip Timing")]
    public float flipDuration = 0.35f;

    [Tooltip("Delay only before FRONT -> BACK animation. Back->Front has NO delay.")]
    public float delayBeforeFlipAnimation = 0.2f;

    [Header("Click-lock (global)")]
    public bool lockClicksForFlipDuration = true;
    public float globalClickLockSeconds = 0.35f;

    static float clicksDisabledUntil = 0f;

    [Header("Audio")]
    public AudioSource audioSource;

    [Tooltip("Separate AudioSource used only for hover keyword sound.")]
    public AudioSource hoverAudioSource;

    [Header("Keyword Sound (hover only)")]
    public AudioClip keywordSound;
    [Range(0f, 1f)] public float keywordVolume = 0.9f;

    [Header("Flip Sound (voice-over)")]
    public AudioClip flipSound;
    [Range(0f, 1f)] public float flipVolume = 1f;

    public bool playBothSimultaneously = false;
    public bool playFlipSoundAfterAnimation = false;
    public float extraDelayBeforeFlipSound = 0f;

    [Header("Definition Sound (front → back only)")]
    public AudioClip definitionSound;
    [Range(0f, 1f)] public float definitionVolume = 1f;
    public float delayBeforeDefinitionSound = 0.25f;

    Coroutine delayedFlipSoundRoutine;
    Coroutine delayedDefinitionRoutine;
    Coroutine delayedVoiceRoutine;

    bool isFlipped = false;
    bool flipping = false;

    [Header("Hover Settings")]
    public float hoverCooldownSeconds = 0.5f;
    float lastHoverPlayTime = -999f;

    // -------------------------------------------------------------------------
    void OnEnable()
    {
        if (!allCards.Contains(this)) allCards.Add(this);
    }

    void OnDisable()
    {
        if (allCards.Contains(this)) allCards.Remove(this);
        CancelPendingAudio();

        if (currentlyPlayingHoverSource == hoverAudioSource)
            currentlyPlayingHoverSource = null;
    }

    void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        if (hoverAudioSource == null)
        {
            hoverAudioSource = gameObject.AddComponent<AudioSource>();
            hoverAudioSource.playOnAwake = false;
            hoverAudioSource.spatialBlend = 0f;
        }
    }

    public void CancelPendingAudio()
    {
        if (delayedFlipSoundRoutine != null) StopCoroutine(delayedFlipSoundRoutine);
        if (delayedDefinitionRoutine != null) StopCoroutine(delayedDefinitionRoutine);
        if (delayedVoiceRoutine != null) StopCoroutine(delayedVoiceRoutine);

        delayedFlipSoundRoutine = null;
        delayedDefinitionRoutine = null;
        delayedVoiceRoutine = null;
    }

    public static void CancelAllPendingAudioOnAllCards()
    {
        foreach (var c in allCards)
            if (c != null) c.CancelPendingAudio();
    }

    public void Setup(string keyword, Sprite infoSprite, string infoText)
    {
        frontKeyword.text = keyword;
        backImage.sprite = infoSprite;
        backInfo.text = infoText;

        isFlipped = false;
        flipping = false;

        frontKeyword.gameObject.SetActive(true);
        backSide.SetActive(false);
        transform.localEulerAngles = Vector3.zero;
    }

    static void SetGlobalClickLock(float seconds)
    {
        float until = Time.time + seconds;
        if (until > clicksDisabledUntil)
            clicksDisabledUntil = until;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Time.time < clicksDisabledUntil) return;
        if (!flipping)
            StartCoroutine(FlipCoroutine());
    }

    // =========================
    // HOVER AUDIO (GLOBAL SAFE)
    // =========================
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isFlipped || flipping || keywordSound == null) return;
        if (Time.time - lastHoverPlayTime < hoverCooldownSeconds) return;

        lastHoverPlayTime = Time.time;

        // Stop previous hover sound globally
        if (currentlyPlayingHoverSource != null &&
            currentlyPlayingHoverSource != hoverAudioSource)
        {
            currentlyPlayingHoverSource.Stop();
        }

        hoverAudioSource.Stop();
        hoverAudioSource.PlayOneShot(keywordSound, keywordVolume);
        currentlyPlayingHoverSource = hoverAudioSource;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hoverAudioSource.isPlaying)
            hoverAudioSource.Stop();

        if (currentlyPlayingHoverSource == hoverAudioSource)
            currentlyPlayingHoverSource = null;
    }

    IEnumerator FlipCoroutine()
    {
        flipping = true;

        float lockSeconds = lockClicksForFlipDuration
            ? flipDuration + delayBeforeFlipAnimation
            : globalClickLockSeconds;

        SetGlobalClickLock(lockSeconds);

        bool fromFront = !isFlipped;

        StopAllAudioSourcesInScene();
        CancelAllPendingAudioOnAllCards();
        CancelPendingAudio();

        if (fromFront && flipSound != null && audioSource != null)
            audioSource.PlayOneShot(flipSound, flipVolume);

        if (fromFront && delayBeforeFlipAnimation > 0f)
            yield return new WaitForSeconds(delayBeforeFlipAnimation);

        float half = flipDuration / 2f;
        float t = 0f;

        while (t < half)
        {
            t += Time.deltaTime;
            transform.localEulerAngles = Vector3.Lerp(Vector3.zero, new Vector3(0, 90, 0), t / half);
            yield return null;
        }

        isFlipped = !isFlipped;
        frontKeyword.gameObject.SetActive(!isFlipped);
        backSide.SetActive(isFlipped);

        if (fromFront && definitionSound != null)
            delayedDefinitionRoutine = StartCoroutine(PlayDelayedDefinitionSound(delayBeforeDefinitionSound));

        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            transform.localEulerAngles = Vector3.Lerp(new Vector3(0, 90, 0), Vector3.zero, t / half);
            yield return null;
        }

        transform.localEulerAngles = Vector3.zero;
        flipping = false;
    }

    void StopAllAudioSourcesInScene()
    {
        foreach (var s in FindObjectsOfType<AudioSource>())
        {
            if (s != hoverAudioSource)
                s.Stop();
        }
    }

    IEnumerator PlayDelayedDefinitionSound(float delay)
    {
        yield return new WaitForSeconds(delay);
        delayedDefinitionRoutine = null;
        audioSource.PlayOneShot(definitionSound, definitionVolume);
    }
}
 