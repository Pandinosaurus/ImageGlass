import { getChangedSettingsFromTab } from '@/helpers';
import { ToolbarEditorHtmlElement } from './webComponents/ToolbarEditorHtmlElement';
import { ToolbarButtonEditDialogHtmlElement } from './webComponents/ToolbarButtonEditDialogHtmlElement';
import { IToolbarButton } from '@/@types/FrmSettings';

export default class TabToolbar {
  static #toolbarEditor = query<ToolbarEditorHtmlElement>('#ToolbarEditor');
  static #toolbarBtnDialog = query<ToolbarButtonEditDialogHtmlElement>('[is="edit-toolbar-dialog"]');

  /**
   * Gets current selected theme.
   */
  static get currentTheme() {
    return _pageSettings.themeList.find(i => i.FolderName === _page.theme);
  }


  /**
   * Loads settings for tab Toolbar.
   */
  static loadSettings() {
    const toolbarEditor = TabToolbar.#toolbarEditor;
    if (!toolbarEditor) return;

    toolbarEditor.initialize(TabToolbar.onEditToolbarButton);
  }


  /**
   * Adds events for tab Toolbar.
   */
  static addEvents() {
    const addBtnEl = query('#Btn_AddCustomToolbarButton');
    const resetBtnEl = query('#Btn_ResetToolbarButtons');

    addBtnEl?.addEventListener('click', TabToolbar.onBtnAddCustomToolbarButtonClick, false);
    resetBtnEl?.addEventListener('click', TabToolbar.onBtnResetToolbarButtonsClick, false);
  }


  /**
   * Save settings as JSON object.
   */
  static exportSettings() {
    const settings = getChangedSettingsFromTab('toolbar');
    const toolbarEditor = TabToolbar.#toolbarEditor;
    if (!toolbarEditor) return settings;

    if (toolbarEditor.hasChanges) {
      settings.ToolbarButtons = toolbarEditor.currentButtons;
    }
    else {
      delete settings.ToolbarButtons;
    }

    return settings;
  }


  private static async onBtnResetToolbarButtonsClick() {
    const toolbarEditor = TabToolbar.#toolbarEditor;
    if (!toolbarEditor) return;

    const defaultToolbarIds = await postAsync<string[]>('Btn_ResetToolbarButtons');
    toolbarEditor.loadItemsByIds(defaultToolbarIds);
  }

  private static async onBtnAddCustomToolbarButtonClick() {
    const toolbarEditor = TabToolbar.#toolbarEditor;
    const toolbarBtnDialog = TabToolbar.#toolbarBtnDialog;
    if (!toolbarEditor || !toolbarBtnDialog) return;

    const isSubmitted = await toolbarBtnDialog.openCreate();
    if (!isSubmitted) return;

    const data = toolbarBtnDialog.getDialogData();
    const btn = JSON.parse(data.ButtonJson) as IToolbarButton;
    const themeBtnIconUrl = TabToolbar.currentTheme?.ToolbarIcons?.[btn.Image] ?? '';

    // image is theme icon
    if (themeBtnIconUrl) {
      btn.ImageUrl = themeBtnIconUrl;
    }
    else {
      // image is an external file
      const imgUrl = new URL(`file:///${btn.Image}`);
      btn.ImageUrl = imgUrl.toString();
    }

    toolbarEditor.insertItems(btn, 0);
  }

  private static async onEditToolbarButton(toolbarBtn: IToolbarButton) {
    const toolbarBtnDialog = TabToolbar.#toolbarBtnDialog;
    if (!toolbarBtnDialog) return null;

    const isSubmitted = await toolbarBtnDialog.openEdit(toolbarBtn);
    if (!isSubmitted) return null;

    const data = toolbarBtnDialog.getDialogData();
    const btn = JSON.parse(data.ButtonJson) as IToolbarButton;
    const themeBtnIconUrl = TabToolbar.currentTheme?.ToolbarIcons?.[btn.Image] ?? '';

    // image is theme icon
    if (themeBtnIconUrl) {
      btn.ImageUrl = themeBtnIconUrl;
    }
    else {
      // image is an external file
      const imgUrl = new URL(`file:///${btn.Image}`);
      btn.ImageUrl = imgUrl.toString();
    }

    return btn;
  }
}
