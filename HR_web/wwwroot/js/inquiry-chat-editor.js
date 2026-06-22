// Tiptap rich editor for inquiry chat input. Loaded as ES module per view.
// Exposes window.InquiryEditor with getHTML/getText/getLength/clear/setHTML/focus.
import { Editor }     from 'https://esm.sh/@tiptap/core@2';
import StarterKit     from 'https://esm.sh/@tiptap/starter-kit@2';
import Underline      from 'https://esm.sh/@tiptap/extension-underline@2';
import TextStyle      from 'https://esm.sh/@tiptap/extension-text-style@2';
import Color          from 'https://esm.sh/@tiptap/extension-color@2';
import Highlight      from 'https://esm.sh/@tiptap/extension-highlight@2';
import Link           from 'https://esm.sh/@tiptap/extension-link@2';
import Placeholder    from 'https://esm.sh/@tiptap/extension-placeholder@2';

const target = document.getElementById('msgEditor');
if (target) {
    const editor = new Editor({
        element: target,
        extensions: [
            StarterKit.configure({
                heading: false, codeBlock: false, code: false,
                blockquote: false, horizontalRule: false
            }),
            Underline,
            TextStyle, Color,
            Highlight.configure({ multicolor: false }),
            Link.configure({ openOnClick: false, autolink: true }),
            Placeholder.configure({ placeholder: 'Nhập tin nhắn... (Enter để gửi, Shift+Enter để xuống dòng)' })
        ],
        editorProps: {
            attributes: { class: 'editor-content' },
            handleKeyDown(view, event) {
                if (event.key === 'Enter' && !event.shiftKey) {
                    event.preventDefault();
                    if (window.InquiryChat?.sendMessage) window.InquiryChat.sendMessage();
                    return true;
                }
                return false;
            }
        },
        onUpdate({ editor }) {
            const len = editor.getText().length;
            if (window.InquiryChat?.onEditorUpdate) window.InquiryChat.onEditorUpdate(len);
        }
    });

    // ─── Toolbar wiring ──────────────────────────────────────
    const cmd = (action, args) => editor.chain().focus()[action](args).run();

    document.getElementById('ed-bold')?.addEventListener('click',      () => cmd('toggleBold'));
    document.getElementById('ed-italic')?.addEventListener('click',    () => cmd('toggleItalic'));
    document.getElementById('ed-underline')?.addEventListener('click', () => cmd('toggleUnderline'));
    document.getElementById('ed-strike')?.addEventListener('click',    () => cmd('toggleStrike'));
    document.getElementById('ed-ul')?.addEventListener('click',        () => cmd('toggleBulletList'));
    document.getElementById('ed-ol')?.addEventListener('click',        () => cmd('toggleOrderedList'));
    document.getElementById('ed-highlight')?.addEventListener('click', () => editor.chain().focus().toggleHighlight({ color: '#fef08a' }).run());
    document.getElementById('ed-clear-fmt')?.addEventListener('click', () => editor.chain().focus().unsetAllMarks().clearNodes().run());
    document.getElementById('ed-color')?.addEventListener('input',     e => editor.chain().focus().setColor(e.target.value).run());

    document.getElementById('ed-link')?.addEventListener('click', () => {
        const prev = editor.getAttributes('link').href || '';
        const url  = prompt('Nhập URL:', prev || 'https://');
        if (url === null) return;
        if (url === '') { editor.chain().focus().unsetLink().run(); return; }
        editor.chain().focus().extendMarkRange('link').setLink({ href: url, target: '_blank' }).run();
    });
    document.getElementById('ed-unlink')?.addEventListener('click', () => editor.chain().focus().unsetLink().run());

    function syncActive() {
        const map = {
            'ed-bold':      editor.isActive('bold'),
            'ed-italic':    editor.isActive('italic'),
            'ed-underline': editor.isActive('underline'),
            'ed-strike':    editor.isActive('strike'),
            'ed-ul':        editor.isActive('bulletList'),
            'ed-ol':        editor.isActive('orderedList'),
            'ed-highlight': editor.isActive('highlight'),
            'ed-link':      editor.isActive('link')
        };
        for (const [id, on] of Object.entries(map)) {
            document.getElementById(id)?.classList.toggle('is-active', !!on);
        }
    }
    editor.on('selectionUpdate', syncActive);
    editor.on('transaction',     syncActive);

    window.InquiryEditor = {
        getHTML:   () => editor.isEmpty ? '' : editor.getHTML(),
        getText:   () => editor.getText(),
        getLength: () => editor.getText().length,
        setHTML:   (html) => editor.commands.setContent(html || ''),
        clear:     () => editor.commands.clearContent(true),
        focus:     () => editor.commands.focus()
    };

    // Trigger initial char counter update
    if (window.InquiryChat?.onEditorUpdate) window.InquiryChat.onEditorUpdate(0);
}
