import { getChangedSettingsFromTab } from '@/helpers';

export default class TabViewer {
  /**
   * Loads settings for tab Viewer.
   */
  static loadSettings() {
    const zoomLevels = _pageSettings.config.ZoomLevels as number[] || [];
    const zoomLevelsEl = query<HTMLTextAreaElement>('[name="ZoomLevels"]');
    const useSmoothZoomingEl = query<HTMLInputElement>('[name="_UseSmoothZooming"]');
    if (!zoomLevelsEl || !useSmoothZoomingEl) return;

    zoomLevelsEl.value = zoomLevels.join('; ');
    useSmoothZoomingEl.checked = zoomLevels.length === 0;
    TabViewer.onUseSmoothZoomingChanged();
  }


  /**
   * Adds events for tab Viewer.
   */
  static addEvents() {
    const useSmoothZoomingEl = query('[name="_UseSmoothZooming"]');
    const zoomLevelsEl = query('[name="ZoomLevels"]');
    const loadDefaultEl = query('#LnkLoadDefaultZoomLevels');

    useSmoothZoomingEl?.addEventListener('input', TabViewer.onUseSmoothZoomingChanged, false);
    zoomLevelsEl?.addEventListener('input', TabViewer.handleZoomLevelsChanged, false);
    zoomLevelsEl?.addEventListener('blur', TabViewer.handleZoomLevelsBlur, false);
    loadDefaultEl?.addEventListener('click', TabViewer.onLoadDefaultZoomLevelsClicked, false);
  }


  /**
   * Save settings as JSON object.
   */
  static exportSettings() {
    const settings = getChangedSettingsFromTab('viewer');
    const zoomLevelsEl = query<HTMLTextAreaElement>('[name="ZoomLevels"]');

    // ZoomLevels
    settings.ZoomLevels = TabViewer.getZoomLevels();

    if (zoomLevelsEl?.checkValidity()) {
      const originalLevelsString = _pageSettings.config.ZoomLevels?.toString();
      const newLevelsString = settings.ZoomLevels?.toString();

      if (newLevelsString === originalLevelsString) {
        delete settings.ZoomLevels;
      }
    }
    else {
      delete settings.ZoomLevels;
    }

    return settings;
  }


  private static onUseSmoothZoomingChanged() {
    const useSmoothZoomingEl = query<HTMLInputElement>('[name="_UseSmoothZooming"]');
    const zoomLevelsEl = query('[name="ZoomLevels"]');
    const loadDefaultEl = query('#LnkLoadDefaultZoomLevels');
    if (!useSmoothZoomingEl || !zoomLevelsEl || !loadDefaultEl) return;

    const isDisabled = useSmoothZoomingEl.checked;

    zoomLevelsEl.toggleAttribute('disabled', isDisabled);
    loadDefaultEl.toggleAttribute('disabled', isDisabled);
    loadDefaultEl.setAttribute('tabindex', isDisabled ? '-1' : '0');
  }


  /**
   * Handle when ZoomLevels is changed.
   */
  private static handleZoomLevelsChanged() {
    const el = query<HTMLTextAreaElement>('[name="ZoomLevels"]');
    if (!el) return;

    const levels = TabViewer.getZoomLevels();

    // validate
    if (levels.some(i => !Number.isFinite(i))) {
      el.setCustomValidity('Value contains invalid characters. Only number, semi-colon are allowed.');
    }
    else {
      el.setCustomValidity('');
    }
  }


  /**
   * Handle when the ZoomLevels box is blur.
   */
  private static handleZoomLevelsBlur() {
    const el = query<HTMLTextAreaElement>('[name="ZoomLevels"]');
    if (!el) return;

    if (!el.checkValidity()) return;

    el.value = TabViewer.getZoomLevels().join('; ');
  }


  /**
   * Gets zoom levels
   */
  private static getZoomLevels() {
    const useSmoothZoomingEl = query<HTMLInputElement>('[name="_UseSmoothZooming"]');
    if (!useSmoothZoomingEl?.checked) {
      const el = query<HTMLTextAreaElement>('[name="ZoomLevels"]');
      const levels = el?.value.split(';').map(i => i.trim()).filter(Boolean)
        .map(i => parseFloat(i)) ?? [];

      return levels;
    }

    return [];
  }


  private static onLoadDefaultZoomLevelsClicked() {
    const defaultLevels = '5; 10; 15; 20; 30; 40; 50; 60; 70; 80; 90; 100; 125; 150; 175; 200; 250; 300; 350; 400; 500; 600; 700; 800; 1000; 1200; 1500; 1800; 2100; 2500; 3000; 3500; 4500; 6000; 8000; 10000';

    const zoomLevelsEl = query<HTMLTextAreaElement>('[name="ZoomLevels"]');
    if (!zoomLevelsEl) return;

    zoomLevelsEl.value = defaultLevels;
  }
}
