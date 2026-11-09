// Paths are relative to DOCS_BASE on both documentation hosts.
export const movedPages = {
    features: {
        to: '',
        title: 'Sylvan handbook',
        sections: {'for-users': 'for-members', 'for-staff': 'staff-handbook'},
    },
    'features/working-hours': {to: 'staff/working-hours/', title: 'Set your working hours'},
    'commands/working-hours': {to: 'staff/working-hours/', title: 'Set your working hours'},
    'help/working-hours': {
        to: 'staff/working-hours/#if-your-hours-arent-taking-effect',
        title: 'Troubleshoot your working hours',
    },
    'commands/working-conditions': {to: 'setup/working-hours/', title: 'Set up working hours'},
    'commands/sylvanmail': {
        to: 'setup/sylvanmail/',
        title: 'Set up ModMail',
        sections: {
            'set-forum-access': 'prepare-the-private-forum',
            'change-the-destination': 'change-the-forum-or-notification-role',
        },
    },
};

export function isLegacyUrl(url, base) {
    const prefix = base.replace(/\/$/, '') + '/';
    const path = new URL(url).pathname;
    return path.startsWith(prefix) && Object.hasOwn(movedPages, path.slice(prefix.length).replace(/\/$/, ''));
}
