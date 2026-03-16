/**
 * Props for `ExternalLink`.
 */
type ExternalLinkProps = {
	/**
	 * Absolute destination URL.
	 */
	href: string;
	/**
	 * Link text shown to users.
	 */
	label: string;
};

/**
 * Renders a standardized external anchor with safe defaults.
 */
export function ExternalLink({ href, label }: ExternalLinkProps) {
	return <a href={href} target="_blank" rel="noreferrer">{label}</a>;
}
