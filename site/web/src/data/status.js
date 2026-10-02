// What each release-tied label says. src/data/products.json holds which label each product has.
export const statuses = {
  beta: { label: 'Beta', variant: 'success', meaning: 'A published download, pre-1.0.' },
  building: { label: 'Building', variant: 'note', meaning: 'In the source, not downloadable yet.' },
  planned: { label: 'Planned', variant: 'default', meaning: 'Committed in the roadmap.' },
};
