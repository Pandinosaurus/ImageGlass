import { getChangedSettingsFromTab } from '@/helpers';

export default class TabSlideshow {
  /**
   * Loads settings for tab Slideshow.
   */
  static loadSettings() {
    TabSlideshow.handleUseRandomIntervalForSlideshowChanged();
    TabSlideshow.handleSlideshowIntervalsChanged();
    TabSlideshow.handleSlideshowBackgroundColorChanged();
  }


  /**
   * Adds events for tab Slideshow.
   */
  static addEvents() {
    const useRandomEl = query('[name="UseRandomIntervalForSlideshow"]');
    const fromEl = query('[name="SlideshowInterval"]');
    const toEl = query('[name="SlideshowIntervalTo"]');
    const resetColorEl = query('#Lnk_ResetSlideshowBackgroundColor');
    const btnColorEl = query('#Btn_SlideshowBackgroundColor');

    useRandomEl?.addEventListener('input', TabSlideshow.handleUseRandomIntervalForSlideshowChanged, false);
    fromEl?.addEventListener('input', TabSlideshow.handleSlideshowIntervalsChanged, false);
    toEl?.addEventListener('input', TabSlideshow.handleSlideshowIntervalsChanged, false);
    resetColorEl?.addEventListener('click', TabSlideshow.resetSlideshowBackgroundColor, false);
    btnColorEl?.addEventListener('click', TabSlideshow.onBtn_SlideshowBackgroundColor, false);
  }


  /**
   * Save settings as JSON object.
   */
  static exportSettings() {
    return getChangedSettingsFromTab('slideshow');
  }


  /**
   * Handle when slideshow intervals are changed.
   */
  private static handleSlideshowIntervalsChanged() {
    const fromEl = query<HTMLInputElement>('[name="SlideshowInterval"]');
    const toEl = query<HTMLInputElement>('[name="SlideshowIntervalTo"]');
    const useRandomEl = query<HTMLInputElement>('[name="UseRandomIntervalForSlideshow"]');
    const lblIntervalEl = query('#Lbl_SlideshowInterval');
    if (!fromEl || !toEl || !useRandomEl || !lblIntervalEl) return;

    const useRandomInterval = useRandomEl.checked;

    if (useRandomInterval) {
      fromEl.max = toEl.value;
      toEl.min = fromEl.value;
    }
    else {
      fromEl.max = '';
    }    

    const intervalFrom = +fromEl.value || 5;
    const intervalTo = +toEl.value || 5;
    const intervalFromText = TabSlideshow.toTimeString(intervalFrom);
    const intervalToText = TabSlideshow.toTimeString(intervalTo);

    if (useRandomInterval) {
      lblIntervalEl.innerText = `${intervalFromText} - ${intervalToText}`;
    }
    else {
      lblIntervalEl.innerText = intervalFromText;
    }
  }


  /**
   * handle when `UseRandomIntervalForSlideshow` is changed.
   */
  private static handleUseRandomIntervalForSlideshowChanged() {
    const fromEl = query<HTMLInputElement>('[name="SlideshowInterval"]');
    const toEl = query<HTMLInputElement>('[name="SlideshowIntervalTo"]');
    const useRandomEl = query<HTMLInputElement>('[name="UseRandomIntervalForSlideshow"]');
    const lblFromEl = query('#Lbl_SlideshowIntervalFrom');
    const sectionToEl = query('#Section_SlideshowIntervalTo');
    if (!fromEl || !toEl || !useRandomEl || !lblFromEl || !sectionToEl) return;

    const useRandomInterval = useRandomEl.checked;
  
    lblFromEl.hidden = !useRandomInterval;
    sectionToEl.hidden = !useRandomInterval;

    const intervalFrom = +fromEl.value || 5;
    const intervalTo = +toEl.value || 5;
    if (useRandomInterval && intervalFrom > intervalTo) {
      toEl.min = intervalFrom.toString();
      toEl.value = intervalFrom.toString();
    }
  }


  // Formats total seconds to time format: `mm:ss.fff`.
  private static toTimeString(totalSeconds: number) {
    const dt = new Date(totalSeconds * 1000);
    let minutes = dt.getUTCMinutes().toString();
    let seconds = dt.getUTCSeconds().toString();
    const msSeconds = dt.getUTCMilliseconds().toString();

    if (minutes.length < 2) minutes = `0${minutes}`;
    if (seconds.length < 2) seconds = `0${seconds}`;

    return `${minutes}:${seconds}.${msSeconds}`;
  }


  // Reset slideshow background color to black
  private static resetSlideshowBackgroundColor() {
    const colorEl = query<HTMLInputElement>('[name="SlideshowBackgroundColor"]');
    if (!colorEl) return;

    colorEl.value = '#000000';
    TabSlideshow.handleSlideshowBackgroundColorChanged();
  }


  // Handles when `SlideshowBackgroundColor` is changed.
  private static handleSlideshowBackgroundColorChanged() {
    const colorInputEl = query<HTMLInputElement>('[name="SlideshowBackgroundColor"]');
    const colorDisplayEl = query<HTMLInputElement>('#Btn_SlideshowBackgroundColor > .color-display');
    const colorLabelEl = query('#Lbl_SlideshowBackgroundColorValue');
    if (!colorInputEl || !colorDisplayEl || !colorLabelEl) return;

    const colorHex = colorInputEl.value;
    if (!colorHex) return;

    colorDisplayEl.style.setProperty('--color-picker-value', colorHex);
    colorLabelEl.innerText = colorHex;
  }


  private static async onBtn_SlideshowBackgroundColor() {
    const colorEL = query<HTMLInputElement>('[name="SlideshowBackgroundColor"]');
    if (!colorEL) return;

    const colorValue = await postAsync<string>('Btn_SlideshowBackgroundColor', colorEL.value);

    if (colorValue) {
      colorEL.value = colorValue;
      TabSlideshow.handleSlideshowBackgroundColorChanged();
    }
  }
}
